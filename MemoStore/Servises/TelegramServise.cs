using System.Collections.Concurrent;
using System.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MemoStore.Servises;

public sealed class TelegramService : IDisposable
{
    private readonly TelegramBotClient _bot =
        new TelegramBotClient("8823965763:AAGltGw6FTa1xaeGMZVdHEOdAMNHJH9o9wI");

    private const long AdminChatId = 1467648222;

    private readonly CancellationTokenSource _cts = new();

    // Запоминаем уже обработанные нажатия
    private readonly ConcurrentDictionary<string, byte> _processed = new();

    private bool _started;


    public Task StartAsync()
    {
        if (_started)
            return Task.CompletedTask;

        _started = true;

        _bot.StartReceiving(
            updateHandler: HandleUpdate,
            errorHandler: HandleError,
            receiverOptions: new ReceiverOptions
            {
                AllowedUpdates = new[]
                {
                    UpdateType.CallbackQuery
                }
            },
            cancellationToken: _cts.Token
        );

        return Task.CompletedTask;
    }


    private async Task HandleUpdate(
        ITelegramBotClient bot,
        Update update,
        CancellationToken cancellationToken)
    {
        var callback = update.CallbackQuery;

        if (callback == null ||
            callback.Message == null ||
            callback.Data == null)
        {
            return;
        }


        // ОДНО конкретное нажатие обрабатываем только ОДИН раз
        if (!_processed.TryAdd(callback.Id, 0))
            return;


        await bot.AnswerCallbackQuery(
            callbackQueryId: callback.Id,
            cancellationToken: cancellationToken
        );


        string oldText = callback.Message.Text ?? "";


        // =========================
        // ПОДТВЕРДИТЬ
        // =========================

        if (callback.Data == "confirm_order")
        {
            if (oldText.Contains("ЗАМОВЛЕННЯ ПІДТВЕРДЖЕНО") ||
                oldText.Contains("ЗАМОВЛЕННЯ ВІДХИЛЕНО"))
            {
                return;
            }


            await bot.EditMessageText(
                chatId: callback.Message.Chat.Id,
                messageId: callback.Message.Id,
                text: oldText + "\n\n✅ ЗАМОВЛЕННЯ ПІДТВЕРДЖЕНО",

                // Убираем кнопки после нажатия
                replyMarkup: null,

                cancellationToken: cancellationToken
            );

            return;
        }


        // =========================
        // ОТКЛОНИТЬ
        // =========================

        if (callback.Data == "cancel_order")
        {
            if (oldText.Contains("ЗАМОВЛЕННЯ ПІДТВЕРДЖЕНО") ||
                oldText.Contains("ЗАМОВЛЕННЯ ВІДХИЛЕНО"))
            {
                return;
            }


            await bot.EditMessageText(
                chatId: callback.Message.Chat.Id,
                messageId: callback.Message.Id,
                text: oldText + "\n\n❌ ЗАМОВЛЕННЯ ВІДХИЛЕНО",

                // Убираем кнопки после нажатия
                replyMarkup: null,

                cancellationToken: cancellationToken
            );

            return;
        }
    }


    public async Task SendToAdmin(string text)
    {
        var buttons = new InlineKeyboardMarkup(
            new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "✅ Підтвердити",
                        "confirm_order"
                    ),

                    InlineKeyboardButton.WithCallbackData(
                        "❌ Відхилити",
                        "cancel_order"
                    )
                }
            }
        );


        await _bot.SendMessage(
            chatId: AdminChatId,
            text: text,
            replyMarkup: buttons
        );
    }


    private Task HandleError(
        ITelegramBotClient bot,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine(exception);

        return Task.CompletedTask;
    }


    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();

        _cts.Dispose();
    }
}