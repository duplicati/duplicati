using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using TeleSharp.TL;
using TeleSharp.TL.Channels;
using TeleSharp.TL.Messages;
using TLSharp.Core;
using TLSharp.Core.Exceptions;

namespace TelegramTest
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            var apiId = 836138;
            var apiHash = "54374c1b962347aa8f816d174aa12b70";
            var phoneNumber = "+37455325588";
            
            var client = new TelegramClient(apiId, apiHash, new FakeSessionStore());
            await client.ConnectAsync();
            
            var me = (TLUser)null;
            
            if (client.Session?.TLUser == null || client.IsUserAuthorized() == false)
            {
                try
                {
                    var phoneCodeHash = await client.SendCodeRequestAsync(phoneNumber);
                    Console.WriteLine("Input the code");
                    var code = Console.ReadLine();
                    me = await client.MakeAuthAsync(phoneNumber, phoneCodeHash, code);
                }
                catch (CloudPasswordNeededException e)
                {
                    Console.WriteLine($"Type of exception is {e.GetType().Name}");
                    Console.WriteLine("Exception thrown, please input your password");
                
                    var myPassword = Console.ReadLine();
                    var passwordSetting = await client.GetPasswordSetting();
                    me = await client.MakeAuthWithPasswordAsync(passwordSetting, myPassword);
                }
            }
            else
            {
                me = client.Session.TLUser;
            }
            
            var userDialogs = (TLDialogsSlice) await client.GetUserDialogsAsync();

            Console.WriteLine("Please input channel name");
            var channelName = Console.ReadLine()?.Trim(' ');
            var channel = (TLChannel)userDialogs.Chats.FirstOrDefault(chat => chat is TLChannel tlChannel && tlChannel.Title == channelName);
            if (channel == null)
            {
                var newGroup = new TLRequestCreateChannel()
                {
                    Broadcast = false,
                    Megagroup = false,
                    Title = channelName,
                    About = "Some name here"
                };
                await client.SendRequestAsync<dynamic>(newGroup); 
                userDialogs = (TLDialogsSlice) await client.GetUserDialogsAsync();
                channel = (TLChannel)userDialogs.Chats.FirstOrDefault(chat => chat is TLChannel tlChannel && tlChannel.Title == channelName);
            }
            var inputPeerChannel = new TLInputPeerChannel() { ChannelId = channel.Id, AccessHash = (long)channel.AccessHash};

            var hist = await client.GetHistoryAsync(inputPeerChannel, 0, -1, 0, Int32.MaxValue);

            Console.WriteLine("=====================================================================");
            Console.WriteLine("THIS IS:" + channel.Title);
            foreach (var m in (hist as TLChannelMessages).Messages.Where(msg => msg is TLMessage))
                Console.WriteLine(((TLMessage)m).Message);
            
            new ManualResetEvent(false).WaitOne();
        }
        
    }
}