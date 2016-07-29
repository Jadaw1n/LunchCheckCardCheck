using LunchCheckSaldoCheck;
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

namespace ConsoleApplication
{
    public class Program
    {
        private static readonly TelegramBotClient Bot = new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"));
        private static readonly DataStore Data = new DataStore("data.json");

        private static Timer CheckCardTimer;

        public static void Main(string[] args)
        {
            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnReceiveError += BotOnReceiveError;

            var me = Bot.GetMeAsync().Result;

            Console.Title = me.Username;

            CheckCardTimer = Helpers.RunAt(new TimeSpan(16, 53, 00), TimeSpan.FromDays(1), (e) =>
            {
                foreach (var chat in Data.ChatData.Values)
                {
                    chat.Cards.ForEach(async (card) =>
                    {
                        var result = await RetrieveCard(card.CardNumber);

                        if (result == null) return; // TODO do some error checking, and remove card if not valid anymore

                        float saldo = result.Item1;
                        bool status = result.Item2;

                        if (saldo != card.LastSaldo || status != card.IsActive)
                        {
                            try
                            {
                                await Bot.SendTextMessageAsync(chat.Chat.Id, $"Saldo: {saldo:00.00}\nActive: {status}");
                            }
                            catch
                            {
                                // TODO remove chat if that happens?
                            }

                            card.IsActive = status;
                            card.LastSaldo = saldo;
                        }
                    });
                }
            });

            Bot.StartReceiving();
            Console.ReadLine();
            Bot.StopReceiving();
        }

        private static void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs)
        {
            Debugger.Break();
        }

        public enum NextMessage
        {
            Unspecified,
            AddCard,
            RemoveCard,
        }

        private static Dictionary<long, NextMessage> chatNextMessage = new Dictionary<long, NextMessage>();

        private static async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;

            if (message == null || message.Type != MessageType.TextMessage) return;

            var nextMessage = chatNextMessage.FirstOrDefault(c => c.Key == message.Chat.Id).Value;

            switch (nextMessage)
            {
                case NextMessage.AddCard:
                    if (await TryRegisterCardNumber(message))
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

        private const string LUNCHCHECK_URL = "https://www.lunch-card.ch/saldo/saldo.aspx?crd=";
        private static readonly Regex CardRegex = new Regex($"(?:{LUNCHCHECK_URL})?" + "([0-9]{4}) ?([0-9]{4}) ?([0-9]{4}) ?([0-9]{4})");

        // TODO I18n for word parsing
        private static readonly Regex CardStatusRegex = new Regex("Kontostand.*?([0-9]+.[0-9]{2}) CHF.*?Kartenstatus.*?\\<b\\>(.*?)\\</b\\>", RegexOptions.Singleline);

        private static async Task<Tuple<float, bool>> RetrieveCard(string cardNumber)
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

                    return new Tuple<float, bool>(saldo, status);
                }
            }
            return null;
        }

        private static async System.Threading.Tasks.Task<bool> TryRegisterCardNumber(Message message)
        {
            Match m = CardRegex.Match(message.Text);

            if (!m.Success) return false;

            string card = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value;

            var result = await RetrieveCard(card);

            if (result == null) return false;

            float saldo = result.Item1;
            bool status = result.Item2;

            await Bot.SendTextMessageAsync(message.Chat.Id, $"Saldo: {saldo:00.00}\nActive: {status}");

            ChatData chat;
            if (!Data.ChatData.TryGetValue(message.Chat.Id, out chat))
            {
                Data.ChatData[message.Chat.Id] = chat = new ChatData { Chat = message.Chat };
            }

            if (!chat.Cards.Any(c => c.CardNumber == card))
            {
                chat.Cards.Add(new Card { CardNumber = card, LastSaldo = saldo, IsActive = true });
            }

            return true;
        }

        private static async void ProcessTextMessage(Message message)
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