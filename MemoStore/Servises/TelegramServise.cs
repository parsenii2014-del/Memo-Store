
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

    private readonly ConcurrentDictionary<string, byte> _processed = new();

    private readonly ConcurrentDictionary<long, string> _waitingForQuantity = new();

    private readonly SemaphoreSlim _adminLock = new(1, 1);

    private bool _started = false;


    // ЗАПУСК

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
                    UpdateType.CallbackQuery,
                    UpdateType.Message
                }
            },
            cancellationToken: _cts.Token
        );

        return Task.CompletedTask;
    }


    // ОБРАБОТКА

    private async Task HandleUpdate(
        ITelegramBotClient bot,
        Update update,
        CancellationToken cancellationToken)
    {
        await _adminLock.WaitAsync(cancellationToken);

        try
        {
            // СООБЩЕНИЯ

            if (update.Message is { } message)
            {
                if (message.Chat.Id != AdminChatId ||
                    message.From?.Id != AdminChatId ||
                    message.Text == null)
                {
                    return;
                }

                string text = message.Text.Trim();

                // Команда /products

                if (text == "/products")
                {
                    _waitingForQuantity.TryRemove(
                        AdminChatId, out _
                    );

                    await SendProducts(
                        AdminChatId,
                        cancellationToken
                    );

                    return;
                }

                // Ожидаем количество

                if (_waitingForQuantity.TryGetValue(
                    AdminChatId, out string? product))
                {
                    if (!int.TryParse(text, out int quantity) ||
                        quantity < 0)
                    {
                        await bot.SendMessage(
                            chatId: AdminChatId,
                            text: "❌ Введи правильну кількість (0 або більше).",
                            cancellationToken: cancellationToken
                        );

                        return;
                    }

                    // Обновляем остаток

                    switch (product)
                    {
                        case "cx08":
                            Products.CX08 = quantity;
                            break;

                        case "cx20":
                            Products.CX20 = quantity;
                            break;

                        case "cx14":
                            Products.CX14 = quantity;
                            break;
                    }

                    _waitingForQuantity.TryRemove(
                        AdminChatId, out _
                    );

                    // Отправляем обновлённые товары

                    await SendProducts(
                        AdminChatId,
                        cancellationToken
                    );

                    return;
                }

                // На остальные сообщения не отвечаем
                return;
            }


            // НАЖАТИЯ КНОПОК

            var callback = update.CallbackQuery;

            if (callback == null ||
                callback.Message == null ||
                callback.Data == null)
            {
                return;
            }

            if (callback.From.Id != AdminChatId ||
                callback.Message.Chat.Id != AdminChatId)
            {
                return;
            }

            if (!_processed.TryAdd(callback.Id, 0))
                return;

            await bot.AnswerCallbackQuery(
                callbackQueryId: callback.Id,
                cancellationToken: cancellationToken
            );


            // КНОПКА ИЗМЕНИТЬ КОЛИЧЕСТВО

            if (callback.Data.StartsWith("edit_"))
            {
                string product = callback.Data.Substring(5);

                string productName = product switch
                {
                    "cx08" => "MEMO CX08 Pro Max",
                    "cx20" => "MEMO CX20",
                    "cx14" => "MEMO CX14",
                    _ => ""
                };

                if (productName == "")
                    return;

                _waitingForQuantity[AdminChatId] = product;

                await bot.SendMessage(
                    chatId: AdminChatId,
                    text: "✏️ Введи кількість для " + productName + ":",
                    cancellationToken: cancellationToken
                );

                return;
            }


            // КНОПКИ ЗАКАЗОВ

            string oldText = callback.Message.Text ?? "";

            if (oldText.Contains("ЗАМОВЛЕННЯ ПІДТВЕРДЖЕНО") ||
                oldText.Contains("ЗАМОВЛЕННЯ ВІДХИЛЕНО"))
            {
                return;
            }


            if (callback.Data == "confirm_order")
            {
                await bot.EditMessageText(
                    chatId: callback.Message.Chat.Id,
                    messageId: callback.Message.Id,
                    text: oldText + "\n\n✅ ЗАМОВЛЕННЯ ПІДТВЕРДЖЕНО",
                    replyMarkup: null,
                    cancellationToken: cancellationToken
                );

                return;
            }


            if (callback.Data == "cancel_order")
            {
                await bot.EditMessageText(
                    chatId: callback.Message.Chat.Id,
                    messageId: callback.Message.Id,
                    text: oldText + "\n\n❌ ЗАМОВЛЕННЯ ВІДХИЛЕНО",
                    replyMarkup: null,
                    cancellationToken: cancellationToken
                );

                return;
            }
        }
        finally
        {
            _adminLock.Release();
        }
    }


    // ОТПРАВКА ТРЁХ ТОВАРОВ

    private async Task SendProducts(
        long chatId,
        CancellationToken cancellationToken)
    {
        string site = "https://memostore.com.ua/";

        await _bot.SendPhoto(
            chatId: chatId,
            photo: InputFile.FromUri(site + "CX08PRO.png"),
            caption:
                " MEMO CX08 Pro Max\n" +
                "Кількість на складі: " + Products.CX08 + " шт.",
            replyMarkup: ProductButton("cx08"),
            cancellationToken: cancellationToken
        );

        await _bot.SendPhoto(
            chatId: chatId,
            photo: InputFile.FromUri(site + "CX20.png"),
            caption:
                " MEMO CX20\n" +
                "Кількість на складі: " + Products.CX20 + " шт.",
            replyMarkup: ProductButton("cx20"),
            cancellationToken: cancellationToken
        );

        await _bot.SendPhoto(
            chatId: chatId,
            photo: InputFile.FromUri(site + "CX14.png"),
            caption:
                " MEMO CX14\n" +
                "Кількість на складі: " + Products.CX14 + " шт.",
            replyMarkup: ProductButton("cx14"),
            cancellationToken: cancellationToken
        );
    }


    // КНОПКА ПОД ТОВАРОМ

    private InlineKeyboardMarkup ProductButton(string product)
    {
        return new InlineKeyboardMarkup(
            new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "✏️ Змінити кількість",
                        "edit_" + product
                    )
                }
            }
        );
    }


    // ОТПРАВКА ЗАКАЗА АДМИНИСТРАТОРУ

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


    // ОШИБКИ

    private Task HandleError(
        ITelegramBotClient bot,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine(exception);

        return Task.CompletedTask;
    }


    // ОСТАНОВКА

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();

        _cts.Dispose();
        _adminLock.Dispose();
    }
}
