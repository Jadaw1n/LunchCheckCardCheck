using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace WebApplication
{
    public class DataStore
    {
        public Dictionary<long, ChatData> ChatData { get; set; } = new Dictionary<long, ChatData>();
    }

    public class ChatData
    {
        public List<Card> Cards { get; } = new List<Card>();
        public Telegram.Bot.Types.Chat Chat { get; set; }
    }

    public class Card
    {
        public string CardNumber { get; set; }
        public float LastSaldo { get; set; }
        public bool IsActive { get; set; }
    }
}