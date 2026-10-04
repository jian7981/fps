using UnityEngine;
using UnityEngine.UI;

public static class ToolFunction
{
    /// <summary>
    /// 使用 Vector3 设置 Image 的颜色
    /// </summary>
    /// <param name="img">需要设置颜色的 Image 组件</param>
    /// <param name="colorVector">包含 RGB 值的 Vector3</param>
    /// <param name="alpha">透明度值（0 到 1之间）</param>
    public static void SetImageColorFromVector3(Image img, Vector3 colorVector, float alpha)
    {
        if (img == null)
        {
            Debug.LogWarning("Image is null!");
            return;
        }

        // 确保 alpha 值在 0 到 1 之间
        alpha = Mathf.Clamp01(alpha);

        // 创建新的 Color，使用 Vector3 的分量作为 RGB，传入 alpha 值
        Color newColor = new Color(colorVector.x, colorVector.y, colorVector.z, alpha);

        // 设置 Image 的颜色
        img.color = newColor;
    }
    public static void SetRawImageColorFromVector3(RawImage img, Vector3 colorVector, float alpha)
    {
        if (img == null)
        {
            Debug.LogWarning("Image is null!");
            return;
        }

        // 确保 alpha 值在 0 到 1 之间
        alpha = Mathf.Clamp01(alpha);

        // 创建新的 Color，使用 Vector3 的分量作为 RGB，传入 alpha 值
        Color newColor = new Color(colorVector.x, colorVector.y, colorVector.z, alpha);

        // 设置 Image 的颜色
        img.color = newColor;
    }
    public static void SetRawImageColorFromVector3(RawImage img, Color colorVector, float alpha)
    {
        if (img == null)
        {
            Debug.LogWarning("Image is null!");
            return;
        }

        // 确保 alpha 值在 0 到 1 之间
        alpha = Mathf.Clamp01(alpha);

        // 创建新的 Color，使用 Vector3 的分量作为 RGB，传入 alpha 值
        Color newColor = colorVector;

        // 设置 Image 的颜色
        img.color = newColor;
    }


    /// <summary>
    /// 计算角色正前方和目标角度的夹角,范围（-180，180）
    /// </summary>
    /// <param name="player"></param>
    /// <param name="toDir"></param>
    /// <returns></returns>
    public static float GetDeltaAngle(Transform player,float targetAngle)
    {
        Vector3 targetDir = Quaternion.Euler(0, targetAngle, 0) * Vector3.forward;
        return GetDeltaAngle(player, targetDir);
    }
    /// <summary>
    /// 计算角色正前方和目标方向的夹角,范围（-180，180）
    /// </summary>
    /// <param name="player"></param>
    /// <param name="toDir"></param>
    /// <returns></returns>
    public static float GetDeltaAngle(Transform player, Vector3 toDir)
    {
        return GetDeltaAngle(player.forward, toDir);
    }
    /// <summary>
    /// 计算两个向量得夹角，忽略Y向量，范围（-180，180）
    /// </summary>
    /// <param name="startDir"></param>
    /// <param name="toDir"></param>
    /// <returns></returns>
    public static float GetDeltaAngle(Vector3 startDir, Vector3 toDir)
    {
        float playerAngle = Mathf.Atan2(startDir.x, startDir.z) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(toDir.x, toDir.z) * Mathf.Rad2Deg;
        float angleDelta = Mathf.DeltaAngle(playerAngle, targetAngle);
        return angleDelta;//正负代表右左
    }
    /// <summary>
    /// 计算跳跃的初速度
    /// </summary>
    /// <returns></returns>
    public static float GetJumpInitVelocity(float jumpMaxHeight,float Gravity)
    {
        return Mathf.Sqrt(-2 * Gravity * jumpMaxHeight);
    }

    public static void MatchTarget(float startTIme,float endTime)
    {

    }
   
}