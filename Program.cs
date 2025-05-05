using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Telegram.Bot;
using Telegram.Bot.Types;
class Program
{
    static readonly Dictionary<long, (DateTime time, string forecast)> weatherCache = new();
    static readonly string weatherKey = "095972f1d5dc35a4e48fd5f1ef8f28e0";
    static readonly string geoKey = "d57a5ce0214e487f9f4707f1af453e9e";
    static readonly string botToken = "7825176743:AAH2FGhVW0p6NP0XCO_vTA4c3aT-YbdV0R8";
    static readonly TelegramBotClient bot = new TelegramBotClient(botToken);
    static readonly HttpClient http = new HttpClient();
    static async Task Main()
    {
        InitDb();
        await bot.SetMyCommandsAsync(new[]
        {
            new Telegram.Bot.Types.BotCommand { Command = "start", Description = "Запуск бота" },
            new Telegram.Bot.Types.BotCommand { Command = "setcity", Description = "Встановити місто" },
            new Telegram.Bot.Types.BotCommand { Command = "weather", Description = "Погода + Що вдягнути + Місця" },
            new Telegram.Bot.Types.BotCommand { Command = "subscribe", Description = "Підписатися на щоденні поради" },
            new Telegram.Bot.Types.BotCommand { Command = "unsubscribe", Description = "Відписатися від щоденних порад" },
            new Telegram.Bot.Types.BotCommand { Command = "history", Description = "Історія запитів" },
            new Telegram.Bot.Types.BotCommand { Command = "support", Description = "Написати в техпідтримку" },
            new Telegram.Bot.Types.BotCommand { Command = "addfavorite", Description = "Додати місце в улюблені" },
            new Telegram.Bot.Types.BotCommand { Command = "favorites", Description = "Переглянути улюблені місця" },
            new Telegram.Bot.Types.BotCommand { Command = "removefavorite", Description = "Видалити місце з улюблених" }
        });
        bot.StartReceiving(UpdateHandler, ErrorHandler);
        Console.WriteLine("🤖 Бот працює. Натисни Enter для виходу...");
        Console.ReadLine();
    }
    static async Task UpdateHandler(ITelegramBotClient client, Telegram.Bot.Types.Update update, CancellationToken token)
    {
        if (update.Message == null || update.Message.Text == null) return;
        long id = update.Message.From.Id;
        string text = update.Message.Text;
        SaveHistory(id, text);
        string startMsg = "👋 <b>Привіт! Я — бот-помічник WeatherWear & Explore</b> 🌤️\n\n" +
                "Ось що я вмію 👇\n" +
                "🏙️ <b>/setcity [місто]</b> — встановити місто за замовчуванням\n" +
                "☁️ <b>/weather</b> — показати погоду + що вдягнути + місця для прогулянки\n" +
                "📬 <b>/subscribe</b> — щоденна порада + прогноз на день\n" +
                "🚫 <b>/unsubscribe</b> — відписатися від щоденних порад\n" +
                "📜 <b>/history</b> — показати останні запити\n" +
                "🛠️ <b>/support [повідомлення]</b> — надіслати звернення в техпідтримку\n" +
                "⭐ <b>/addfavorite [назва місця]</b> — додати улюблене місце\n" +
                "📋 <b>/favorites</b> — показати улюблені місця\n" +
                "🗑️ <b>/removefavorite [назва місця]</b> — видалити улюблене місце\n\n" +
                "✨ Просто введи команду й користуйся зручністю!";
        string cityNotSetMsg = "⚠️ Встановіть спочатку місто за допомогою /setcity\n\nНаприклад: /setcity Kyiv";
        string cityNotFoundMsgTemplate = "❗ Не вдалося знайти погоду для міста '{0}'. Переконайтесь, що місто введено правильно.\n\nПриклад: /setcity Lviv або /setcity Kyiv";
        if (text.StartsWith("/start"))
        {
            var keyboard = new Telegram.Bot.Types.ReplyMarkups.ReplyKeyboardMarkup(new[]
            {
                new[] {
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/weather"),
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/setcity")
                },
                new[] {
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/subscribe"),
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/unsubscribe")
                },
                new[] {
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/favorites"),
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/addfavorite")
                },
                new[] {
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/removefavorite"),
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/history")
                },
                new[] {
                    new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("/support")
                }
            })
            {
                ResizeKeyboard = true
            };
            await bot.SendTextMessageAsync(id, startMsg, Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: keyboard);
        }
        else if (text.StartsWith("/setcity"))
        {
            string city = text.Replace("/setcity", "").Trim();
            if (string.IsNullOrWhiteSpace(city))
            {
                await bot.SendTextMessageAsync(id, "❗ Вкажіть місто. Приклад: /setcity Lviv");
                return;
            }
            if (city.Length < 2)
            {
                await bot.SendTextMessageAsync(id, "❗ Назва міста занадто коротка. Спробуйте ще раз.");
                return;
            }
            Exec($"REPLACE INTO Users VALUES ({id}, '{city}')");
            await bot.SendTextMessageAsync(id, $"🏙️ Місто встановлено: {city}");
        }
        else if (text.StartsWith("/subscribe"))
        {
            if (Exists($"SELECT 1 FROM Subscriptions WHERE user_id={id}"))
                await bot.SendTextMessageAsync(id, "📩 Ви вже підписані ✅");
            else
            {
                Exec($"REPLACE INTO Subscriptions VALUES ({id}, {id})");
                await bot.SendTextMessageAsync(id, "✅ Підписка активна! Щодня надсилатимемо прогноз ☀️");
            }
        }
        else if (text.StartsWith("/unsubscribe"))
        {
            if (!Exists($"SELECT 1 FROM Subscriptions WHERE user_id={id}"))
                await bot.SendTextMessageAsync(id, "📭 Ви не підписані на розсилку.");
            else
            {
                Exec($"DELETE FROM Subscriptions WHERE user_id={id}");
                await bot.SendTextMessageAsync(id, "❎ Ви відписалися від щоденних порад.");
            }
        }
        else if (text.StartsWith("/weather"))
        {
            if (weatherCache.TryGetValue(id, out var cached) && (DateTime.Now - cached.time).TotalMinutes < 5)
            {
                await bot.SendTextMessageAsync(id, cached.forecast);
                return;
            }
            string city = GetVal($"SELECT default_city FROM Users WHERE id={id}") ?? "";
            if (string.IsNullOrWhiteSpace(city))
            {
                await bot.SendTextMessageAsync(id, cityNotSetMsg);
                return;
            }
            string url = $"https://api.openweathermap.org/data/2.5/weather?q={city}&appid={weatherKey}&units=metric&lang=ua";
            var response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                await bot.SendTextMessageAsync(id, string.Format(cityNotFoundMsgTemplate, city));
                return;
            }
            var res = await response.Content.ReadFromJsonAsync<Weather>();
            string desc = res.weather?[0].description ?? "н/д";
            float temp = res.main?.temp ?? 0;
            string outfit;
            if (temp >= 35)
                outfit = "🩳 Дуже легкий одяг + 🧢 Панама + 🧴 Сонцезахисний крем + 🕶️ Окуляри";
            else if (temp >= 30)
                outfit = "🩳 Легкий одяг + 🧢 Кепка + 🕶️ Окуляри";
            else if (temp >= 25)
                outfit = "👕 Футболка + 👖 Легкі штани або шорти";
            else if (temp >= 20)
                outfit = "👕 Футболка + 👖 Штани або джинси";
            else if (temp >= 15)
                outfit = "👕 Лонгслів + 👖 Джинси";
            else if (temp >= 10)
                outfit = "🧥 Легка куртка + 👖 Джинси або штани";
            else if (temp >= 5)
                outfit = "🧥 Тепла куртка + 🧤 Легкі рукавиці";
            else if (temp >= 0)
                outfit = "🧥 Зимова куртка + 🧣 Шарф + 🧤 Рукавиці + 🧢 Тепла шапка";
            else if (temp >= -10)
                outfit = "🧥 Дуже тепла куртка + 🧣 Шарф + 🧤 Теплі рукавиці + 🧢 Тепла шапка + 🥾 Зимове взуття";
            else
                outfit = "🧥 Екстримально теплий одяг + ❄️ Термобілизна + 🥾 Тепле взуття + 🧣 Шарф + 🧤 Рукавиці + 🧢 Тепла шапка";
            if (desc.Contains("дощ"))
                outfit += " + ☂️ Парасоля!";
            if (desc.Contains("сніг"))
                outfit += " + 🥾 Тепле водонепроникне взуття!";
            if (desc.Contains("вітер"))
                outfit += " + 🧥 Вітрозахисна куртка!";
            string placeList = await GetPlaces(city);
            string msg = $"🌤️ {city}: {desc}, {temp}°C\n{outfit}\n📍 Що подивитись:\n{placeList}";
            weatherCache[id] = (DateTime.Now, msg);
            await bot.SendTextMessageAsync(id, msg);
        }
        else if (text.StartsWith("/history"))
        {
            var list = GetList($"SELECT message FROM History WHERE user_id={id} ORDER BY datetime DESC LIMIT 5");
            await bot.SendTextMessageAsync(id, "📜 Історія:\n" + string.Join("\n", list));
        }
        else if (text.StartsWith("/addfavorite"))
        {
            string place = text.Replace("/addfavorite", "").Trim();
            if (string.IsNullOrWhiteSpace(place))
            {
                await bot.SendTextMessageAsync(id, "❗ Введіть назву місця. Приклад: /addfavorite Central Park");
                return;
            }

            // Перевірка чи місце вже існує
            bool exists = Exists($"SELECT 1 FROM Favorites WHERE user_id={id} AND place_name='{place.Replace("'", "''")}'");

            if (exists)
            {
                await bot.SendTextMessageAsync(id, "⭐ Це місце вже є у ваших улюблених!");
            }
            else
            {
                Exec($"INSERT INTO Favorites VALUES ({id}, '{place.Replace("'", "''")}', datetime('now'))");
                await bot.SendTextMessageAsync(id, "✅ Місце успішно додано в улюблені!");
            }
        }
        else if (text.StartsWith("/favorites"))
        {
            var favList = GetList($"SELECT place_name FROM Favorites WHERE user_id={id} ORDER BY datetime DESC LIMIT 10");
            if (favList.Count == 0)
                await bot.SendTextMessageAsync(id, "📭 У вас поки що немає улюблених місць.");
            else
                await bot.SendTextMessageAsync(id, "⭐ Ваші улюблені місця:\n" + string.Join("\n", favList));
        }
        else if (text.StartsWith("/removefavorite"))
        {
            var favList = GetList($"SELECT place_name FROM Favorites WHERE user_id={id} ORDER BY datetime DESC LIMIT 10");
            if (favList.Count == 0)
            {
                await bot.SendTextMessageAsync(id, "📭 У вас поки що немає улюблених місць.");
                return;
            }

            string place = text.Replace("/removefavorite", "").Trim();
            if (string.IsNullOrWhiteSpace(place))
            {
                await bot.SendTextMessageAsync(id, "❗ Вкажіть назву місця для видалення. Ваші улюблені місця:\n" +
                    string.Join("\n", favList) + "\n\nПриклад: /removefavorite Central Park");
                return;
            }

            bool exists = Exists($"SELECT 1 FROM Favorites WHERE user_id={id} AND place_name='{place.Replace("'", "''")}'");
            if (!exists)
            {
                await bot.SendTextMessageAsync(id, "📭 Такого місця немає у ваших улюблених.");
            }
            else
            {
                Exec($"DELETE FROM Favorites WHERE user_id={id} AND place_name='{place.Replace("'", "''")}'");
                await bot.SendTextMessageAsync(id, "🗑️ Місце видалено з улюблених!");
            }
        }
        else if (text.StartsWith("/support"))
        {
            string msg = text.Replace("/support", "").Trim();
            if (string.IsNullOrWhiteSpace(msg))
            {
                await bot.SendTextMessageAsync(id, "❗ Введіть повідомлення для техпідтримки.\nПриклад: /support У мене не працює команда /weather");
                return;
            }

            Exec($"INSERT INTO Support VALUES ({id}, '{msg.Replace("'", "''")}', datetime('now'))");
            await bot.SendTextMessageAsync(id, "📨 Звернення надіслано. Дякуємо!");
        }
        else if (text.StartsWith("/adminsupport"))
        {
            long adminId = 1390937778;
            if (id != adminId)
            {
                await bot.SendTextMessageAsync(id, "❌ У вас немає прав для перегляду звернень.");
                return;
            }
            var supportList = GetList("SELECT user_id || ': ' || message || ' (' || datetime || ')' FROM Support ORDER BY datetime DESC LIMIT 5");
            if (supportList.Count == 0)
            {
                await bot.SendTextMessageAsync(id, "📭 Немає нових звернень.");
            }
            else
            {
                await bot.SendTextMessageAsync(id, "🛠️ Останні звернення:\n\n" + string.Join("\n\n", supportList));
            }
        }
    }
    static Task ErrorHandler(ITelegramBotClient client, Exception ex, CancellationToken token)
    {
        Console.WriteLine("❌ " + ex.Message);
        return Task.CompletedTask;
    }
    static void InitDb()
    {
        Exec("CREATE TABLE IF NOT EXISTS Users (id INTEGER PRIMARY KEY, default_city TEXT);");
        Exec("CREATE TABLE IF NOT EXISTS Subscriptions (user_id INTEGER PRIMARY KEY, chat_id INTEGER);");
        Exec("CREATE TABLE IF NOT EXISTS History (user_id INTEGER, message TEXT, datetime TEXT);");
        Exec("CREATE TABLE IF NOT EXISTS Support (user_id INTEGER, message TEXT, datetime TEXT);");
        Exec("CREATE TABLE IF NOT EXISTS Favorites (user_id INTEGER, place_name TEXT, datetime TEXT);");
    }
    static SqliteConnection OpenDb()
    {
        var db = new SqliteConnection("Data Source=bot.db");
        db.Open();
        return db;
    }
    static void Exec(string sql)
    {
        using var db = OpenDb();
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
    static string GetVal(string sql)
    {
        using var db = OpenDb();
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }
    static bool Exists(string sql)
    {
        return GetVal(sql) != null;
    }
    static void SaveHistory(long id, string text)
    {
        Exec($"INSERT INTO History VALUES ({id}, '{text.Replace("'", "''")}', datetime('now'))");
    }
    static List<string> GetList(string sql)
    {
        var list = new List<string>();
        using var db = OpenDb();
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }
    static async Task<string> GetPlaces(string city)
    {
        try
        {
            var geoBuilder = new UriBuilder
            {
                Scheme = "https",
                Host = "api.geoapify.com",
                Path = "/v1/geocode/search",
                Query = $"text={Uri.EscapeDataString(city)}&apiKey={geoKey}"
            };
            string geoUrl = geoBuilder.ToString();
            var geo = await http.GetFromJsonAsync<GeoResult>(geoUrl);
            if (geo?.features == null || geo.features.Count == 0)
                return "❗ Місто не знайдено. Перевір правильність написання.";
            double lon = geo.features[0].geometry.coordinates[0];
            double lat = geo.features[0].geometry.coordinates[1];
            var placesBuilder = new UriBuilder
            {
                Scheme = "https",
                Host = "api.geoapify.com",
                Path = "/v2/places",
                Query = $"categories=tourism.sights,entertainment.museum,leisure.park&filter=circle:{lon.ToString(CultureInfo.InvariantCulture)},{lat.ToString(CultureInfo.InvariantCulture)},3000&limit=5&apiKey={geoKey}"
            };
            string url = placesBuilder.ToString();
            var data = await http.GetFromJsonAsync<PlaceResult>(url);
            if (data?.features == null || data.features.Count == 0)
                return "📭 У цьому районі немає популярних місць.";
            return string.Join("\n", data.features.Select(x => "🔹 " + x.properties.name));
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Geoapify API error: " + ex.Message);
            return "❌ Вибач, не вдалося знайти цікаві місця. Перевір назву міста або спробуй пізніше.";
        }
    }
    class Weather
    {
        public List<WeatherDescription>? weather { get; set; }
        public MainWeather? main { get; set; }
    }
    class WeatherDescription
    {
        public string? description { get; set; }
    }
    class MainWeather
    {
        public float temp { get; set; }
    }
    class GeoResult
    {
        public List<GeoFeature> features { get; set; }
    }
    class GeoFeature
    {
        public GeoGeometry geometry { get; set; }
    }
    class GeoGeometry
    {
        public List<double> coordinates { get; set; }
    }
    class PlaceResult
    {
        public List<PlaceFeature> features { get; set; }
    }
    class PlaceFeature
    {
        public PlaceProperties properties { get; set; }
    }
    class PlaceProperties
    {
        public string name { get; set; }
    }
}

















































