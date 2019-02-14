using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using System.Threading;

namespace WebApplication
{
    public class TelegramBot
    {
        private readonly Settings settings;
        private readonly TelegramBotClient bot;
        private readonly DataStore dataStore;
        private readonly Regex cardRegex;
        private readonly Regex cardStatusRegex = new Regex("Kontostand.*?([0-9]+.[0-9]{2}) CHF.*?Kartenstatus.*?\\<b\\>(.*?)\\</b\\>", RegexOptions.Singleline);

        public TelegramBot(Settings settings)
        {
            this.settings = settings;

            cardRegex = new Regex($"(?:{settings.LunchcheckBaseUrl})?" + "([0-9]{4}) ?([0-9]{4}) ?([0-9]{4}) ?([0-9]{4})");

            dataStore = InitDatabase();
            bot = InitBot();
            InitCheckCardTimer();

            Console.WriteLine("Bot is ready!");

            Thread.Sleep(Timeout.Infinite);
        }

        private TelegramBotClient InitBot()
        {
            var bot = new TelegramBotClient(settings.BotToken);
            bot.OnMessage += BotOnMessageReceived;
            bot.StartReceiving();

            return bot;
        }

        private void InitCheckCardTimer()
        {
            Action workUnit = async () =>
            {
                foreach (var (chatId, chat) in dataStore.ChatData)
                {
                    foreach (var card in chat.Cards)
                    {
                        try
                        {
                            var (saldo, status) = await RetrieveCard(card.CardNumber);

                            if (saldo != card.LastSaldo || status != card.IsActive)
                            {
                                SendSaldoUpdate(chat.Chat, saldo, status);

                                card.IsActive = status;
                                card.LastSaldo = saldo;
                            }
                        }
                        catch (Exception e)
                        {
                            // TODO do some error checking, and remove card if not valid anymore
                            Console.WriteLine($"Regular Check error: {e}");
                        }
                    }
                }
            };

            var scheduler = new Scheduler(settings.CheckTime, workUnit);
            scheduler.Start();
        }

        private async void SendSaldoUpdate(Chat chat, float saldo, bool status)
        {
            await bot.SendTextMessageAsync(chat.Id, $"Saldo: {saldo:0.00} CHF\nActive: {status}");
        }

        private DataStore InitDatabase()
        {
            DataStore ds;
            try
            {
                using (var file = System.IO.File.OpenText(settings.DataFile))
                {
                    ds = JsonConvert.DeserializeObject<DataStore>(file.ReadToEnd());
                }
                Console.WriteLine($"Restored data from file {settings.DataFile}");
                Console.WriteLine("Active accounts:");
                ds.ChatData.Values.ToList().ForEach(c =>
                {
                    Console.WriteLine($"    {c.Chat.Id} {c.Chat.Username} {c.Chat.FirstName} {c.Chat.LastName}");
                });
            }
            catch
            {
                Console.WriteLine($"Creating new DataStore.");
                ds = new DataStore();
            }

            Action workUnit = () =>
            {
                try
                {
                    var s = JsonConvert.SerializeObject(ds);
                    System.IO.File.WriteAllText(settings.DataFile, s);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error when writing file {settings.DataFile}");
                    Console.WriteLine(e);
                }
            };

            var scheduler = new Scheduler("* * * * *", workUnit);
            scheduler.Start();

            return ds;
        }

        private readonly Dictionary<long, NextMessage> chatNextMessage = new Dictionary<long, NextMessage>();

        private async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;

            if (message == null || message.Type != MessageType.Text) return;

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
                        await bot.SendTextMessageAsync(message.Chat.Id, "Invalid Lunch Check card number. /cancel");
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
            WebRequest webRequest = WebRequest.Create(settings.LunchcheckBaseUrl + cardNumber);

            using (var reader = new StreamReader((await webRequest.GetResponseAsync()).GetResponseStream()))
            {
                string responseText = reader.ReadToEnd();
                Match m2 = cardStatusRegex.Match(responseText);

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
            Match m = cardRegex.Match(message.Text);

            if (!m.Success) return false;

            string card = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value;

            try
            {
                var (saldo, status) = await RetrieveCard(card);

                SendSaldoUpdate(message.Chat, saldo, status);

                if (!dataStore.ChatData.TryGetValue(message.Chat.Id, out ChatData chat))
                {
                    dataStore.ChatData[message.Chat.Id] = chat = new ChatData { Chat = message.Chat };
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
                    await bot.SendTextMessageAsync(message.Chat.Id, "Please send me your Lunch Check card number or link scanned from QR code. Or /cancel");
                    chatNextMessage[message.Chat.Id] = NextMessage.AddCard;
                    break;

                case "/about":
                    await bot.SendTextMessageAsync(message.Chat.Id, "This bot was programmed by a swiss guy.");
                    break;

                case "/start":
                    await bot.SendTextMessageAsync(message.Chat.Id, "This bot checks your Lunch Check saldo once per day, and sends you a message if it changed. Press /newcard to register a card.");
                    break;

                case "/cancel":
                    chatNextMessage.Remove(message.Chat.Id);
                    await bot.SendTextMessageAsync(message.Chat.Id, "Current operation canceled.");
                    break;

                default:
                    break;
            }
        }
    }
}