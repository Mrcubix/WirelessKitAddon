using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StreamJsonRpc;

namespace WirelessKitAddon.Lib.RPC
{
    public class RpcServer<T> : IDisposable
        where T : class, new()
    {
        private JsonRpc rpc;
        private readonly string pipeName;

        private bool hasStarted = false;

        public RpcServer(string pipeName)
        {
            this.pipeName = pipeName;
            this.rpc = null!;
            this.ConnectionStateChanged = null!;
        }

        public RpcServer(string pipeName, T instance)
        {
            this.pipeName = pipeName;
            this.rpc = null!;
            this.ConnectionStateChanged = null!;
            this.Instance = instance;
        }

        public event EventHandler<bool> ConnectionStateChanged;

        public T Instance { protected set; get; } = new T();
        public List<JsonConverter> Converters { get; } = new List<JsonConverter>();

        public async Task MainAsync()
        {
            if (hasStarted)
                return;
            else
                hasStarted = true;

            while (hasStarted)
            {
                var stream = CreateStream();
                await stream.WaitForConnectionAsync();
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        ConnectionStateChanged?.Invoke(this, true);
                        this.rpc = GetRpc(stream, Instance);
                        await this.rpc.Completion;
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (Exception)
                    {
                    }

                    ConnectionStateChanged?.Invoke(this, false);
                    this.rpc.Dispose();
                    await stream.DisposeAsync();
                });
            }
        }

        private NamedPipeServerStream CreateStream()
        {
            return new NamedPipeServerStream(
                this.pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough | PipeOptions.CurrentUserOnly
            );
        }

        private JsonRpc GetRpc(NamedPipeServerStream stream, T Instance)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // If no converters are provided, use the default JsonRpc
            if (Converters.Count == 0)
                return JsonRpc.Attach(stream, Instance);

            // Otherwise, create a custom JsonRpc with the provided converters
            var messageFormatter = new JsonMessageFormatter();

            foreach (var converter in Converters)
                messageFormatter.JsonSerializer.Converters.Add(converter);

            var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream, messageFormatter), Instance);
            rpc.StartListening();

            return rpc;
        }

        public void Dispose()
        {
            hasStarted = false;
            rpc?.Dispose();
        }
    }
}