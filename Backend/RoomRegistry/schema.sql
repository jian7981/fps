-- ============================================================
-- 《Blow up your brother》后端数据库初始化脚本
--
-- 用法（本机用 root 跑一次即可）：
--   mysql -uroot -p --default-character-set=utf8mb4 < schema.sql
--
-- 本脚本**幂等**：全部 CREATE ... IF NOT EXISTS，重复执行不会报错。
-- ============================================================
--
-- 【全局约定】以后加表一律照这套来，风格统一才好维护：
--   1. 表名/列名用小写 snake_case；主键统一叫 id（关联列叫 xxx_id）
--   2. 主键统一 BIGINT UNSIGNED AUTO_INCREMENT（别用 INT，早晚不够）
--   3. 时间列统一 DATETIME(6)、**一律存 UTC**（接口返回的也是 UTC，客户端自己转本地）
--   4. 每张表尽量带 created_at；会变的表带 updated_at ... ON UPDATE CURRENT_TIMESTAMP(6)
--   5. 关联列一律建外键 + ON DELETE CASCADE（游戏数据没有"留着孤儿行"的必要）
--   6. 新功能 = **新增表**，不往现有表里堆业务列（下面"扩展点"有例子）
--
-- 【编码与排序规则】
--   库默认 utf8mb4_0900_ai_ci —— MySQL 8 的默认排序规则，按 Unicode 排序规则比较，
--   对中文和 emoji 都比老的 general_ci 更正确；仍然是"大小写不敏感 + 重音不敏感"，
--   所以 username / nickname 的唯一索引天然把 Alice 和 alice 当作同一个名字。
-- ============================================================

CREATE DATABASE IF NOT EXISTS blowyourbrother
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_0900_ai_ci;

USE blowyourbrother;

-- ------------------------------------------------------------
-- 1) 账号（只管"认证"这一件事，个人资料/战绩都不放这里）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  username      VARCHAR(32)     NOT NULL                COMMENT '登录名（唯一、大小写不敏感）',
  password_hash VARBINARY(32)   NOT NULL                COMMENT 'PBKDF2(SHA-256) 派生密钥，绝不存明文',
  salt          VARBINARY(16)   NOT NULL                COMMENT '每个用户独立的随机盐',
  iterations    INT             NOT NULL DEFAULT 210000 COMMENT '迭代次数单独存：以后提高强度不影响老密码',
  nickname      VARCHAR(32)     NOT NULL                COMMENT '游戏内显示名；唯一是为了以后能按名字加好友',
  status        TINYINT UNSIGNED NOT NULL DEFAULT 0     COMMENT '0=正常 1=封禁（登录时校验，先留好位置）',
  created_at    DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  last_login_at DATETIME(6)     NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uk_users_username (username),
  UNIQUE KEY uk_users_nickname (nickname)
) ENGINE=InnoDB COMMENT='账号（认证）';

-- ------------------------------------------------------------
-- 2) 玩家累计战绩（一局结束后由服务器上报累加）
-- ------------------------------------------------------------
-- 刻意和 users 拆成两张 1:1 的表：
--   · users 只装"登录才需要"的东西 → 校验账号时永远只碰一张窄表
--   · 战绩以后要加很多列（赛季分、段位、各武器统计…）→ 加在它自己身上，不动认证
-- 注册时就插一行默认值，之后对局结束只做 UPDATE ... SET x = x + ?，不必判"行在不在"。
CREATE TABLE IF NOT EXISTS player_stats (
  user_id    BIGINT UNSIGNED NOT NULL,
  level      INT NOT NULL DEFAULT 1,
  exp        INT NOT NULL DEFAULT 0,
  matches    INT NOT NULL DEFAULT 0,
  wins       INT NOT NULL DEFAULT 0,
  kills      INT NOT NULL DEFAULT 0,
  deaths     INT NOT NULL DEFAULT 0,
  assists    INT NOT NULL DEFAULT 0,
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (user_id),
  CONSTRAINT fk_stats_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='玩家累计战绩（汇总值）';

-- ------------------------------------------------------------
-- 3) 登录令牌
-- ------------------------------------------------------------
-- 库里只存 token 的 SHA-256（数据库泄了也拿不到能用的 token）；原文只在注册/登录响应里回给客户端一次。
-- 用不透明随机 token 而不是 JWT：无状态 JWT 不能即时吊销，这里一行 DELETE 就能登出/踢人。
-- 一个用户可以有多行 → **天然支持多设备同时在线**，不用改表。
-- token_hash 特意用 ascii_bin（精确、区分大小写），避免拿"排序规则不敏感"去做安全比较。
CREATE TABLE IF NOT EXISTS tokens (
  token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'SHA-256(token) 的十六进制',
  user_id    BIGINT UNSIGNED NOT NULL,
  created_at DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  expires_at DATETIME(6)     NOT NULL,
  PRIMARY KEY (token_hash),
  KEY idx_tokens_user (user_id),
  KEY idx_tokens_expires (expires_at),
  CONSTRAINT fk_tokens_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='登录令牌（可多设备）';

-- ------------------------------------------------------------
-- 4) 每局每人的上报记录
-- ------------------------------------------------------------
-- 同时干两件事，所以现在就该有：
--   ① **上报幂等**：复合主键 (match_id, user_id) + INSERT IGNORE ——
--      同一局的重复上报（超时重发、反复点"返回房间"）只会成功一次，战绩不会重复累加；
--   ② **以后"对局历史/战绩明细"的现成挂载点**：这行本身就记下了那一局的原始数字，
--      以后加"最近对局列表/单局详情"直接查它，不用再建表也不用补历史数据。
CREATE TABLE IF NOT EXISTS match_report_log (
  match_id    CHAR(36)        NOT NULL COMMENT '一局对局的 UUID（服务器生成）',
  user_id     BIGINT UNSIGNED NOT NULL,
  team_id     INT             NOT NULL DEFAULT 0,
  kills       INT             NOT NULL DEFAULT 0,
  deaths      INT             NOT NULL DEFAULT 0,
  assists     INT             NOT NULL DEFAULT 0,
  won         TINYINT         NOT NULL DEFAULT 0 COMMENT '1=胜方 0=败方',
  reported_at DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (match_id, user_id),
  KEY idx_mrl_user (user_id, reported_at),
  CONSTRAINT fk_mrl_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='每局每人的上报记录（幂等凭据 + 对局明细）';

-- ------------------------------------------------------------
-- 5) 玩家展示资料（头像）
-- ------------------------------------------------------------
-- 【阶段4-G】为什么单独一张表、而不是往 player_stats 加一列：
--   本文件开头的全局约定第 6 条就是「新功能 = 新增表，不往现有表里堆业务列」。
--   头像属于"外观/展示"，和 level/exp/kills 那堆"战绩"是两码事 ——
--   以后还要往里长"装备槽 / 已解锁外观"，那时改这张表不会碰到任何战绩查询。
--
-- avatar_id 只存**编号**（0 = 默认头像），真正的图片在客户端 AvatarLibrary 资产里按下标取。
-- 为什么存编号而不是图片路径/二进制：
--   · 换美术只改客户端资产，库一行都不用动
--   · 不用处理上传文件的存储、格式校验、敏感图审核（那是另一整块工作）
--   · 传输只有一个 int，连对局里的房间成员同步都省事
-- 代价：头像只能是"预设库里的那几个"。等以后真要自定义上传，再加一张 user_avatars 表存图片。
CREATE TABLE IF NOT EXISTS player_profile (
  user_id    BIGINT UNSIGNED NOT NULL,
  avatar_id  INT NOT NULL DEFAULT 0 COMMENT '预设头像编号（0 = 默认）；要和客户端 AvatarLibrary 的数量对齐',
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (user_id),
  CONSTRAINT fk_profile_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='玩家展示资料（头像）';

-- ------------------------------------------------------------
-- 6) 经验流水（每局每人一行）
-- ------------------------------------------------------------
-- 【阶段4-H】为什么单独记流水，而不是只把经验累加进 player_stats：
--   · "这局到底给了多少经验、升没升级"是玩家会追问的信息（历史战绩里就显示 +EXP）
--   · 以后做"经验来源分析 / 补发 / 客服核对"时，累计值根本查不出是哪一局出的问题
--   · 复合主键 (user_id, match_id) 天然和 match_report_log 的幂等闸门同构：
--     即使上报接口将来改逻辑，同一个 (人, 局) 也绝不会写两行流水
CREATE TABLE IF NOT EXISTS exp_ledger (
  user_id      BIGINT UNSIGNED NOT NULL,
  match_id     CHAR(36)        NOT NULL COMMENT '一局对局的 UUID（服务器生成）',
  exp_gain     INT             NOT NULL DEFAULT 0 COMMENT '本局实际入账经验（已封顶）',
  level_before INT             NOT NULL DEFAULT 1 COMMENT '上报前的等级',
  level_after  INT             NOT NULL DEFAULT 1 COMMENT '上报后的等级',
  created_at   DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (user_id, match_id),
  KEY idx_exp_ledger_time (user_id, created_at),
  CONSTRAINT fk_exp_ledger_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='经验流水（每局每人一行）';

-- ------------------------------------------------------------
-- 7) 玩家每日标记（目前只用来认领"每日首胜"）
-- ------------------------------------------------------------
-- 为什么按"天"存一行、而不是在 users 里存 last_first_win_day：
--   · 老表不加业务列（全局约定第 6 条）
--   · 一行 = 一天，天然保留"哪天来过"的历史；以后做签到/每日任务直接在这张表上长
-- day 用 CHAR(10) 存 UTC+8 的 YYYY-MM-DD：
--   · 中国玩家的"今天"必须按北京时间切，用 UTC 切会在早上 8 点换日，玩家会觉得莫名其妙
--   · 用字符串而不是 DATE，是为了让时区偏移完全在应用层决定，数据库不掺和时区
CREATE TABLE IF NOT EXISTS player_daily (
  user_id         BIGINT UNSIGNED NOT NULL,
  day             CHAR(10)        NOT NULL COMMENT 'UTC+8 的日期 YYYY-MM-DD',
  first_win_bonus TINYINT         NOT NULL DEFAULT 0 COMMENT '1 = 当天首胜奖励已领',
  created_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at      DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (user_id, day),
  CONSTRAINT fk_player_daily_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='玩家每日标记（首胜）';

-- ------------------------------------------------------------
-- 8) 玩家角色 / 武器选择（大厅展示 + 战斗外观）
-- ------------------------------------------------------------
-- 按本文件全局约定：新功能 = 新增表，不往 users / player_stats 塞业务列。
-- 目前只落地“角色选择”；selected_weapon_id 先留好，后续做武器选择直接复用。
-- 服务端只存角色 id（和 Unity CharacterCatalog 的 id 对齐），不存预制体路径：
--   换模型/换美术只改客户端资产，数据库一行都不用动。
CREATE TABLE IF NOT EXISTS player_loadout (
  user_id                BIGINT UNSIGNED NOT NULL,
  selected_character_id  VARCHAR(64)     NOT NULL DEFAULT 'xbot'
                         COMMENT '角色 id；必须和客户端 CharacterCatalog 的白名单一致',
  selected_weapon_id     VARCHAR(64)     NULL
                         COMMENT '预留：大厅展示/战斗武器 id',
  updated_at             DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                         ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (user_id),
  CONSTRAINT fk_loadout_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='玩家角色/武器选择（外观与大厅展示）';

-- ============================================================
-- 扩展点：以后加功能时"加表"而不是"改表"（下面是说明书，不是待执行的建表语句）
-- ============================================================
--
-- ▸ 好友系统（已规划）
--   新增一张 friendships 即可，现有四张表一行都不用改：
--     CREATE TABLE friendships (
--       user_id    BIGINT UNSIGNED NOT NULL,   -- 约定 user_id < friend_id（小的放前面）
--       friend_id  BIGINT UNSIGNED NOT NULL,
--       status     TINYINT NOT NULL DEFAULT 0, -- 0=待接受 1=已接受 2=拉黑
--       created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
--       updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
--       PRIMARY KEY (user_id, friend_id),      -- 复合主键 + "小 id 在前"= 一条关系只存一行
--       KEY idx_friendships_friend (friend_id),-- 反向查"谁加了我"，否则要全表扫
--       FOREIGN KEY (user_id)   REFERENCES users(id) ON DELETE CASCADE,
--       FOREIGN KEY (friend_id) REFERENCES users(id) ON DELETE CASCADE
--     ) ENGINE=InnoDB;
--   要点：一条关系**只存一行**（双向好友是同一行、靠 status 表示），
--         否则"双向两行"迟早不一致（一边删了另一边还在）。
--   想加好友申请留言，再配一张 friend_requests 表存触发者/留言/过期时间。
--
-- ▸ 每局完整明细（比分/时长/双方阵容）
--   加 matches（一局一行）+ match_players（一局一人一行）。
--   player_stats 继续当"累计汇总"，两者职责不冲突。
--   注意：现在 match_report_log 已经在记"每人每局"的原始数字，够用就不必急着拆表。
--
-- ▸ 皮肤 / 背包 / 装备
--   加 player_items / player_loadout，关联列都是 user_id，与 users 是 1:N。
--
-- ▸ 第三方登录（微信/QQ/Steam）
--   加 user_identities(provider, open_id, user_id)，**不要把厂商字段塞进 users**——
--   否则每接一家就要 ALTER TABLE。
--
-- ▸ 排行榜
--   先别急着建汇总表；数据量小的时候直接查 player_stats 即可。
--   真要优化时再加索引（例如 KEY idx_stats_kills (kills)）或做定时汇总表。
--
-- ⚠️ 明确**不放数据库**的东西（属易失数据，放了反而要处理过期/清理）：
--   · 在线状态、玩家当前所在房间 → 放内存（房间登记表本来就在内存里）
--   · 房间码 → 同上；房间一停就该消失，不该落库
-- ============================================================

-- ------------------------------------------------------------
-- 业务专用账号（最小权限）
-- ------------------------------------------------------------
-- 只给这一个库的增删改查，不给建表/删库权限（应用跑起来后不该有能力改 schema）。
-- 后端连接串默认就用这个账号（见 Backend/RoomRegistry/Db.cs）。
-- ⚠️ 注意：加新表/改表要用 root 跑本脚本；byb_app 没有 DDL 权限。
CREATE USER IF NOT EXISTS 'byb_app'@'%' IDENTIFIED BY 'BybApp9x7K2q';
GRANT SELECT, INSERT, UPDATE, DELETE ON blowyourbrother.* TO 'byb_app'@'%';
FLUSH PRIVILEGES;

SELECT '数据库初始化完成' AS result;
