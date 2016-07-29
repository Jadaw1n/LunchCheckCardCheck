using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LunchCheckSaldoCheck
{
    public class DataStore
    {
        public Dictionary<long, ChatData> ChatData { get; set; }
        private readonly Timer Timer;

        public DataStore(string jsonFile)
        {
            try
            {
                string file = File.ReadAllText(jsonFile);

                ChatData = JsonConvert.DeserializeObject<Dictionary<long, ChatData>>(file);
            }
            catch
            {
                ChatData = new Dictionary<long, ChatData>();
            }

            Timer = new Timer((e) =>
            {
                var s = JsonConvert.SerializeObject(ChatData);
                File.WriteAllText(jsonFile, s);
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }
    }

    public class ChatData
    {
        public List<Card> Cards { get; set; } = new List<Card>();
        public Telegram.Bot.Types.Chat Chat { get; set; }
    }

    public class Card
    {
        public string CardNumber { get; set; }
        public float LastSaldo { get; set; }
        public bool IsActive { get; set; }
    }
}