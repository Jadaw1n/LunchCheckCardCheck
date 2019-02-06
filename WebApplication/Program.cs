using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace WebApplication
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .AddUserSecrets<Program>(true);

            var config = configBuilder.Build();

            var settings = config.Get<Settings>();

            new TelegramBot(settings);
        }
    }
}
