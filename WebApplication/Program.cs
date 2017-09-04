using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Threading;

namespace WebApplication
{
    public class Program
    {
        public static void Main(string[] args)
        {
            new TelegramBot();
        }
    }
}
