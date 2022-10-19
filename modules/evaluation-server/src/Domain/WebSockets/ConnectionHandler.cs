using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Domain.WebSockets;

public partial class ConnectionHandler : IConnectionHandler
{
    private readonly IConnectionManager _connectionManager;
    private readonly MessageReader _messageReader;
    private readonly IMessageHandler _messageHandler;
    private readonly ILogger<ConnectionHandler> _logger;

    public ConnectionHandler(
        IConnectionManager connectionManager,
        IMessageHandler messageHandler,
        ILogger<ConnectionHandler> logger)
    {
        _connectionManager = connectionManager;

        _messageReader = new MessageReader();
        _messageReader.OnMessageAsync += OnMessageAsync;
        _messageReader.OnError += OnMessageError;

        _messageHandler = messageHandler;

        _logger = logger;
    }

    public async Task OnConnectedAsync(Connection connection, CancellationToken cancellationToken)
    {
        // add connection
        _connectionManager.Add(connection);

        // start listen websocket message
        var ws = connection.WebSocket;
        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _messageReader.StartAsync(connection, cancellationToken);
            }
            catch (WebSocketException e) when (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                ws.Abort();
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                Log.ErrorProcessMessage(_logger, connection.Id, ex.Message);
            }
        }

        // close connection
        await connection.CloseAsync(
            ws.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
            ws.CloseStatusDescription ?? string.Empty,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        // remove connection
        _connectionManager.Remove(connection);
    }

    public async Task OnMessageAsync(Connection connection, Message message, CancellationToken cancellationToken)
    {
        if (message.Type == WebSocketMessageType.Close)
        {
            Log.ReceiveCloseMessage(_logger, connection.Id);
            return;
        }

        if (message.Bytes.IsEmpty)
        {
            Log.ReceiveEmptyMessage(_logger, connection.Id);
            return;
        }

        // currently we only process text messages
        if (message.Type == WebSocketMessageType.Text)
        {
            await _messageHandler.HandleAsync(connection, message, cancellationToken);
        }
    }

    public void OnMessageError(Connection connection, string error)
    {
        Log.ErrorReadMessage(_logger, connection.Id, error);
    }
}