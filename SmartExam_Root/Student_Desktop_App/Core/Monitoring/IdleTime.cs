namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — seconds since the last keyboard or mouse input, from the two 32-bit millisecond tick counts Windows
/// hands out (<c>GetTickCount</c> and <c>LASTINPUTINFO.dwTime</c>). Both wrap every ~49.7 days, so the difference
/// is taken modulo 2³² (unsigned subtraction) — a PC that has been up for 50 days still reports a small idle time.
/// </summary>
public static class IdleTime
{
    public static int Seconds(uint nowTickMs, uint lastInputTickMs) =>
        (int)Math.Min(int.MaxValue, unchecked(nowTickMs - lastInputTickMs) / 1000u);
}
