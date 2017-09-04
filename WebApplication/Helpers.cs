using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WebApplication
{
    public static class Helpers
    {

        public static Timer RunAt(TimeSpan alertTime, TimeSpan interval, TimerCallback runFunc)
        {
            DateTime current = DateTime.Now;
            TimeSpan timeToGo = alertTime - current.TimeOfDay;
            if (timeToGo < TimeSpan.Zero)
            {
                timeToGo = timeToGo.Add(TimeSpan.FromDays(1));
            }
            return new Timer(runFunc, null, timeToGo, interval);
        }

    }
}
