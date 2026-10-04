using System;
using System.Collections.Concurrent;
using System.Threading;
//毫秒级定时器
public class TickTimer : GameTimerBase
{
    class TaskCBPack
    {
        public int tid;
        public Action tack;
        public TaskCBPack(int tid, Action tack)
        {
            this.tid = tid;
            this.tack = tack;
        }
    }
    private readonly ConcurrentDictionary<int, TimerTask> taskDic;
    private readonly ConcurrentQueue<TaskCBPack> taskCBPackQue;
    private readonly bool SetHandle;
    private readonly DateTime startDateTime = new DateTime(1970,1,1,1,0,0,0,0);
    private readonly object TimerLock = new object();
    private Thread thread;
    private readonly CancellationTokenSource tokenSource;
    public TickTimer(int interval=0,bool setHandle = false)
    {
        SetHandle = setHandle;
        if (setHandle)
        {
            taskCBPackQue = new ConcurrentQueue<TaskCBPack>();
        }
        taskDic = new ConcurrentDictionary<int, TimerTask>();
        if (interval != 0)
        {
           tokenSource = new CancellationTokenSource();
            void StartTime()
            {
                try
                {
                    while (!tokenSource.IsCancellationRequested)
                    {
                        UpdateTime();
                        Thread.Sleep(interval);
                    }
                }
                catch(ThreadAbortException e )
                {
                    warnFunc?.Invoke("TickTime StartTime Thread Abort Failed"+e);
                }
            }
            thread = new Thread(new ThreadStart(StartTime));
            thread.Start(); // 启动线程
        }
    }

    public void UpdateTime()
    {
        double newTime = GetNewUTCMilliSecond();
        foreach (var task in taskDic)
        {
            TimerTask timerTask = task.Value;
            if (newTime < timerTask.destTime)
            {
                continue;
            }
            ++timerTask.countIndex;
            if (timerTask.count > 0)
            {
                --timerTask.count;
                if (timerTask.count == 0)
                {
                    FinishTimer(timerTask.tid);
                }
                else
                {
                    timerTask.destTime = timerTask.startTime + (timerTask.countIndex + 1) * timerTask.time;
                    CallTaskCB(timerTask.tid, timerTask.taskCB);
                }
            }
            else
            {
                timerTask.destTime = timerTask.startTime + (timerTask.countIndex + 1) * timerTask.time;
                CallTaskCB(timerTask.tid, timerTask.taskCB);
            }
        }
    }
    public void UpdateCBTask()
    {
        if (taskCBPackQue != null)
        {
            while (taskCBPackQue.Count > 0)
            {
                if (taskCBPackQue.TryDequeue(out var taskCBPack))
                {
                    taskCBPack.tack.Invoke();
                }
            }
        }
    }

    private void FinishTimer(int tid)
    {
        if (taskDic.TryRemove(tid, out TimerTask timerTask))
        {
            CallTaskCB(tid,timerTask.taskCB);
        }
        else
        {
            errorFunc($"remove timerTask tid:{tid} failed");
        }
    }
    private void CallTaskCB(int tid,Action taskCB)
    {
        if (SetHandle)
        {
            taskCBPackQue.Enqueue(new TaskCBPack(tid,taskCB));
        }
        else
        {
            taskCB.Invoke();
        }
    }

    public override int AddTimer(int time, Action taskCB, Action cancelCB, int count = 1)
    {
        int tid = GenerateTid();
        double startTime = GetNewUTCMilliSecond();
        double destTime = startTime + time;
        TimerTask timerTask = new TimerTask(tid,time,taskCB,cancelCB,count,startTime,destTime);
        if (taskDic.TryAdd(tid, timerTask))
        {
            return tid;
        }
        else
        {

            return -1;
        }
    }
    public override bool DeleteTimer(int tid)
    {
        if (taskDic.TryRemove(tid,out var timerTask))
        {
            if (SetHandle&&timerTask != null)
            {
                taskCBPackQue.Enqueue(new TaskCBPack(tid, timerTask.cancelCB));
            }
            else
            {
                timerTask.cancelCB?.Invoke();
            }
            return true;
        }
        else
        {
          //  errorFunc?.Invoke($"Tid {tid} remove in  tackDic Failed");
            return false;
        }
    }

    public override void ResetTimer()
    {
       taskDic.Clear();
        if (tokenSource != null)
        {
            tokenSource.Cancel();

            if (thread != null&&thread.IsAlive)
            {
                thread.Join();
            }
        }
      
    }
    private double GetNewUTCMilliSecond()
    {
        TimeSpan ts = DateTime.UtcNow - startDateTime;
        return ts.TotalMilliseconds;
    }
    protected override int GenerateTid()
    {
        lock (taskDic)
        {
            while (true)
            {
                ++tid;
                if (tid > int.MaxValue)
                {
                    tid = 0;
                }
                if (!taskDic.ContainsKey(tid))
                {
                    return tid;
                }
            }
        }
    }
    class TimerTask
    {
        public int tid;
        public int time;
        public Action taskCB;
        public Action cancelCB;
        public int count;
        public double destTime;
        public double startTime;

        public long countIndex;
        public TimerTask(int tid, int time, Action taskCB, Action cancelCB, int count,double startTime, double destTime)
        {
            this.tid = tid;
            this.time = time;
            this.taskCB = taskCB;
            this.cancelCB = cancelCB;
            this.count = count;
            this.destTime = destTime;
            this.startTime = startTime;
            countIndex = 0; 
        }
    }
}
