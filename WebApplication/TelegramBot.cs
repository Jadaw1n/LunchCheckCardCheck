using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WebApplication
{
    public class TelegramBot
    {
        private readonly TelegramBotClient Bot = new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"));
        private readonly TimeSpan CheckCardTimerTime = TimeSpan.Parse(Environment.GetEnvironmentVariable("CARD_CHECK_TIME") ?? "14:00");
        private readonly string DataStoreFile = Environment.GetEnvironmentVariable("DATA_FILE") ?? "data.json";

        private DataStore DataStore { get; set; }

        private const string LUNCHCHECK_URL = "https://www.lunch-card.ch/saldo/saldo.aspx?crd=";
        private readonly Regex CardRegex = new Regex($"(?:{LUNCHCHECK_URL})?" + "([0-9]{4}) ?([0-9]{4}) ?([0-9]{4}) ?([0-9]{4})");

        // TODO I18n for word parsing
        private readonly Regex CardStatusRegex = new Regex("Kontostand.*?([0-9]+.[0-9]{2}) CHF.*?Kartenstatus.*?\\<b\\>(.*?)\\</b\\>", RegexOptions.Singleline);

        public TelegramBot()
        {
            InitDatabase();
            InitCheckCardTimer();

            // init bot
            Bot.OnMessage += BotOnMessageReceived;

            Bot.StartReceiving();
            Console.WriteLine("Bot is ready!");
            Console.ReadLine();
            Bot.StopReceiving();
        }

        private void InitCheckCardTimer()
        {
            Helpers.RunAt(CheckCardTimerTime, TimeSpan.FromDays(1), (e) =>
            {
                DataStore.ChatData.Values.ToList().ForEach(chat => chat.Cards.ForEach(async card =>
                {
                    try
                    {
                        var (saldo, status) = await RetrieveCard(card.CardNumber);

                        if (saldo != card.LastSaldo || status != card.IsActive)
                        {
                            await Bot.SendTextMessageAsync(chat.Chat.Id, $"Saldo: {saldo:00.00} CHF\nActive: {status}");

                            card.IsActive = status;
                            card.LastSaldo = saldo;
                        }
                    }
                    catch
                    {
                        // TODO do some error checking, and remove card if not valid anymore
                    }
                }));
            });
        }

        private void InitDatabase()
        {
            try
            {
                using (var file = System.IO.File.OpenText(DataStoreFile))
                {
                    DataStore = JsonConvert.DeserializeObject<DataStore>(file.ReadToEnd());
                }
                Console.WriteLine($"Restored data from file {DataStoreFile}");
            }
            catch
            {
                Console.WriteLine($"Creating new DataStore.");
                DataStore = new DataStore();
            }

            new Timer(_ =>
            {
                try
                {
                    var s = JsonConvert.SerializeObject(DataStore);
                    System.IO.File.WriteAllText(DataStoreFile, s);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error when writing file {DataStoreFile}");
                    Console.WriteLine(e);
                }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }

        private Dictionary<long, NextMessage> chatNextMessage = new Dictionary<long, NextMessage>();

        private async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;

            if (message == null || message.Type != MessageType.TextMessage) return;

            var nextMessage = chatNextMessage.FirstOrDefault(c => c.Key == message.Chat.Id).Value;

            switch (nextMessage)
            {
                case NextMessage.AddCard:
                    if (await TryRegisterCardNumberAsync(message))
                    {
                        chatNextMessage.Remove(message.Chat.Id);
                    }
                    else
                    {
                        await Bot.SendTextMessageAsync(message.Chat.Id, "Invalid Lunch Check card number. /cancel");
                    }

                    break;

                case NextMessage.Unspecified:
                default:
                    ProcessTextMessage(message);
                    break;
            }
        }

        private async Task<(float saldo, bool status)> RetrieveCard(string cardNumber)
        {
            WebRequest webRequest = WebRequest.Create(LUNCHCHECK_URL + cardNumber);

            using (var reader = new StreamReader((await webRequest.GetResponseAsync()).GetResponseStream()))
            {
                string responseText = reader.ReadToEnd();
                Match m2 = CardStatusRegex.Match(responseText);

                if (m2.Success)
                {
                    float saldo = float.Parse(m2.Groups[1].Value);
                    bool status = m2.Groups[2].Value == "aktiv";

                    return (saldo, status);
                }
            }

            throw new Exception($"Error when trying to get saldo for cardNumber: {cardNumber}");
        }

        private async Task<bool> TryRegisterCardNumberAsync(Message message)
        {
            Match m = CardRegex.Match(message.Text);

            if (!m.Success) return false;

            string card = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value;

            try
            {
                var (saldo, status) = await RetrieveCard(card);

                await Bot.SendTextMessageAsync(message.Chat.Id, $"Saldo: {saldo:00.00}\nActive: {status}");

                if (!DataStore.ChatData.TryGetValue(message.Chat.Id, out ChatData chat))
                {
                    DataStore.ChatData[message.Chat.Id] = chat = new ChatData { Chat = message.Chat };
                }

                if (!chat.Cards.Any(c => c.CardNumber == card))
                {
                    chat.Cards.Add(new Card { CardNumber = card, LastSaldo = saldo, IsActive = status });
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async void ProcessTextMessage(Message message)
        {
            switch (message.Text)
            {
                case "/newcard":
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Please send me your Lunch Check card number or link scanned from QR code. Or /cancel");
                    chatNextMessage[message.Chat.Id] = NextMessage.AddCard;
                    break;

                case "/about":
                    await Bot.SendTextMessageAsync(message.Chat.Id, "This bot was programmed by a swiss guy.");
                    break;

                case "/start":
                    await Bot.SendTextMessageAsync(message.Chat.Id, "This bot checks your Lunch Check saldo once per day, and sends you a message if it changed. Press /newcard to register a card.");
                    break;

                case "/cancel":
                    chatNextMessage.Remove(message.Chat.Id);
                    await Bot.SendTextMessageAsync(message.Chat.Id, "Current operation canceled.");
                    break;

                default:
                    break;
            }
        }
    }
}