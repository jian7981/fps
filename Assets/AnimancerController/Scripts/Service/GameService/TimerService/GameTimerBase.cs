using System;

public abstract class GameTimerBase 
{
    public Action<string> logFunc;
    public Action<string> warnFunc;
    public Action<string> errorFunc;

    public abstract int AddTimer(int time, Action taskCB, Action cancelCB, int count = 1);
    public abstract bool DeleteTimer(int tid);
    public abstract void ResetTimer();
    protected abstract int GenerateTid();

    protected int tid;
}
