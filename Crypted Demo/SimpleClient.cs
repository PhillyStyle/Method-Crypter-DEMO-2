using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Crypted_Demo
{
    public class SimpleClient
    {
        private readonly string _host;
        private readonly int _port;

        public SimpleClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void RunClient()
        {
            bool isRunDecrypted = false;
            bool decryptingRun = false;
            bool isRunStringsDecrypted = false;
            bool decryptingRunStrings = false;

            using (TcpClient client = new TcpClient())
            {
                client.Connect(_host, _port);

                using (NetworkStream stream = client.GetStream())
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    Console.WriteLine("Connected to server.");

                    SendCommand(writer, "login", "myuser", "mypass");

                    foreach (var line in CommandReader.ReadCommands(stream))
                    {
                        Console.WriteLine("Server: " + line);

                        var tokens = Tokenize(line);
                        if (tokens.Count == 0) continue;

                        string cmd = tokens[0].ToLowerInvariant();
                        tokens.RemoveAt(0);

                        if (cmd == "login" && tokens.Count > 0)
                        {
                            if (tokens[0] == "ok")
                            {
                                Console.WriteLine("Login successful.");
                            }
                            else
                            {
                                Console.WriteLine("Login failed.");
                                break;
                            }
                        }
                        else if (cmd == "run")
                        {
                            if (tokens.Count == 0)
                            {
                                SendCommand(writer, "run", "start");

                                if (!isRunDecrypted)
                                {
                                    SendCommand(writer, "decrypt", "run");
                                }
                            }
                        }
                        else if (cmd == "decrypt")
                        {
                            if (tokens.Count > 1 && tokens[0].ToLowerInvariant() == "run")
                            {
                                if (tokens.Count == 2)
                                {
                                    if (tokens[1].ToLowerInvariant() == "begin")
                                    {
                                        decryptingRun = true;
                                    }
                                    else if (tokens[1].ToLowerInvariant() == "end")
                                    {
                                        decryptingRun = false;
                                        isRunDecrypted = true;
                                    }
                                }
                                else if (tokens.Count == 6 && decryptingRun)
                                {
                                    DecryptMethod.DecryptToMethod(int.Parse(tokens[1]), int.Parse(tokens[2]), int.Parse(tokens[3]), Convert.FromBase64String(tokens[4]), Convert.FromBase64String(tokens[5]));
                                }
                            }
                            else if (tokens.Count > 1 && tokens[0].ToLowerInvariant() == "run_strings")
                            {
                                if (tokens[1].ToLowerInvariant() == "begin")
                                {
                                    decryptingRunStrings = true;
                                }
                                else if (tokens[1].ToLowerInvariant() == "end")
                                {
                                    decryptingRunStrings = false;
                                    isRunStringsDecrypted = true;

                                    if (isRunDecrypted && isRunStringsDecrypted)
                                    {
                                        MethodInfo method = typeof(SimpleClient).GetMethod("RunSnakeGame", BindingFlags.NonPublic | BindingFlags.Static); //If method isn't staic use BindingFlags.Instance
                                        ForceReJit(method);
                                        RunSnakeGame(writer);
                                    }

                                    // End run
                                    SendCommand(writer, "run", "end");
                                }
                                else if (tokens.Count == 5 && decryptingRunStrings)
                                {
                                    DecryptMethod.DecryptStringArray(tokens[1], tokens[2], Convert.FromBase64String(tokens[3]), Convert.FromBase64String(tokens[4]));
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Disconnected from server.");
        }

        public static void ForceReJit(MethodInfo method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            RuntimeHelpers.PrepareMethod(method.MethodHandle);

            // Now clear existing native code pointer to make it JIT again next time
            IntPtr methodHandle = method.MethodHandle.Value;
            unsafe
            {
                // target native code pointer slot
                IntPtr* pMethodDesc = (IntPtr*)methodHandle.ToPointer();
                // zeroing out forces re-JIT
                pMethodDesc[1] = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Send formatter: builds a command line with quoted params if needed.
        /// </summary>
        static void SendCommand(StreamWriter writer, string command, params string[] args)
        {
            var sb = new StringBuilder();
            sb.Append(command);

            foreach (var arg in args)
            {
                if (arg == "")
                {
                    sb.Append(" \"\""); // empty string gets sent as quoted empty
                }
                else if (arg.Contains(" "))
                {
                    sb.Append(" \"").Append(arg).Append("\"");
                }
                else
                {
                    sb.Append(" ").Append(arg);
                }
            }

            writer.WriteLine(sb.ToString());
            Console.WriteLine("Sent: " + sb.ToString());
        }


        /// <summary>
        /// Splits a line into tokens, treating quoted strings as one token.
        /// Quotes are removed from results.
        /// </summary>
        static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(input))
                return tokens;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;

                    // Check for empty quoted string
                    if (!inQuotes && sb.Length == 0)
                    {
                        // "" detected, add empty token
                        tokens.Add("");
                    }

                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    // End of unquoted token
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                    else
                    {
                        // ignore multiple spaces
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            // Add last token if any
            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }

        private static void RunSnakeGame(StreamWriter writer)
        {
            SendCommand(writer, "echo", "Running Snake Game!");
            SnakeGame.SnakeGame.Run();
        }
    }

    public static class CommandReader
    {
        public static IEnumerable<string> ReadCommands(NetworkStream stream)
        {
            byte[] buffer = new byte[4096];
            StringBuilder sb = new StringBuilder();

            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                string current = sb.ToString();
                int newlineIndex;
                while ((newlineIndex = current.IndexOf('\n')) >= 0)
                {
                    string command = current.Substring(0, newlineIndex).TrimEnd('\r');
                    yield return command;

                    current = current.Substring(newlineIndex + 1);
                }

                sb.Clear();
                sb.Append(current);
            }
        }
    }
}
