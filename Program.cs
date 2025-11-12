using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

record Hotel(
    string Id,
    string Name,
    List<RoomType> RoomTypes,
    List<Room> Rooms
);

record RoomType(
    string Code,
    string Description,
    List<string> Amenities,
    List<string> Features
);

record Room(
    string RoomType,
    string RoomId
);

record Booking(
    string HotelId,
    string Arrival,
    string Departure,
    string RoomType,
    string RoomRate
);

class Program
{
    static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static void Main(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            Console.WriteLine("Usage: myapp --hotels hotels.json --bookings bookings.json");
            return;
        }

        string hotelsFile = null;
        string bookingsFile = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--hotels" && i + 1 < args.Length) hotelsFile = args[++i];
            if (args[i] == "--bookings" && i + 1 < args.Length) bookingsFile = args[++i];
        }

        if (hotelsFile == null || bookingsFile == null)
        {
            Console.WriteLine("Both --hotels and --bookings arguments are required.");
            return;
        }

        if (!File.Exists(hotelsFile))
        {
            Console.WriteLine($"Hotels file not found: {hotelsFile}");
            return;
        }
        if (!File.Exists(bookingsFile))
        {
            Console.WriteLine($"Bookings file not found: {bookingsFile}");
            return;
        }

        List<Hotel> hotels;
        List<Booking> bookings;
        try
        {
            hotels = JsonSerializer.Deserialize<List<Hotel>>(File.ReadAllText(hotelsFile), jsonOptions) ?? new List<Hotel>();
            bookings = JsonSerializer.Deserialize<List<Booking>>(File.ReadAllText(bookingsFile), jsonOptions) ?? new List<Booking>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading JSON files: {ex.Message}");
            return;
        }

        var hotelRoomTypeCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hotels)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (h.Rooms != null)
            {
                foreach (var r in h.Rooms)
                {
                    if (!dict.ContainsKey(r.RoomType)) dict[r.RoomType] = 0;
                    dict[r.RoomType]++;
                }
            }
            hotelRoomTypeCounts[h.Id] = dict;
        }

        var bookingCalendar = new Dictionary<string, Dictionary<string, Dictionary<DateTime, int>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bookings)
        {
            if (string.IsNullOrWhiteSpace(b.HotelId) || string.IsNullOrWhiteSpace(b.Arrival) || string.IsNullOrWhiteSpace(b.Departure) || string.IsNullOrWhiteSpace(b.RoomType))
                continue;

            if (!TryParseYmd(b.Arrival, out DateTime arr) || !TryParseYmd(b.Departure, out DateTime dep))
                continue;

            for (var d = arr.Date; d < dep.Date; d = d.AddDays(1))
            {
                if (!bookingCalendar.ContainsKey(b.HotelId)) bookingCalendar[b.HotelId] = new Dictionary<string, Dictionary<DateTime, int>>(StringComparer.OrdinalIgnoreCase);
                var hotelDict = bookingCalendar[b.HotelId];
                if (!hotelDict.ContainsKey(b.RoomType)) hotelDict[b.RoomType] = new Dictionary<DateTime, int>();
                var roomDict = hotelDict[b.RoomType];
                if (!roomDict.ContainsKey(d)) roomDict[d] = 0;
                roomDict[d] += 1;
            }
        }

        Console.WriteLine("Hotel availability application. Enter commands. Blank line to exit.");
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) break;

            line = line.Trim();

            try
            {
                if (line.StartsWith("Availability", StringComparison.OrdinalIgnoreCase))
                {
                    var inside = ExtractArgs(line);
                    if (inside == null) { Console.WriteLine("Invalid command format."); continue; }
                    if (inside.Count != 3) { Console.WriteLine("Availability expects 3 arguments."); continue; }
                    var hotelId = inside[0].Trim();
                    var dateArg = inside[1].Trim();
                    var roomType = inside[2].Trim();

                    if (!hotelRoomTypeCounts.ContainsKey(hotelId))
                    {
                        Console.WriteLine($"Hotel not found: {hotelId}");
                        continue;
                    }

                    if (dateArg.Contains("-"))
                    {
                        var parts = dateArg.Split('-', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 2 || !TryParseYmd(parts[0].Trim(), out DateTime start) || !TryParseYmd(parts[1].Trim(), out DateTime end))
                        {
                            Console.WriteLine("Invalid date range. Use YYYYMMDD-YYYYMMDD.");
                            continue;
                        }
                        var nightsStart = start.Date;
                        var nightsEndExclusive = end.Date.AddDays(1);
                        int available = ComputeAvailabilityForRange(hotelId, roomType, nightsStart, nightsEndExclusive, hotelRoomTypeCounts, bookingCalendar);
                        Console.WriteLine(available);
                    }
                    else
                    {
                        if (!TryParseYmd(dateArg, out DateTime day)) { Console.WriteLine("Invalid date. Use YYYYMMDD."); continue; }
                        var dayStart = day.Date;
                        var dayEndExclusive = dayStart.AddDays(1);
                        int available = ComputeAvailabilityForRange(hotelId, roomType, dayStart, dayEndExclusive, hotelRoomTypeCounts, bookingCalendar);
                        Console.WriteLine(available);
                    }
                }
                else if (line.StartsWith("Search", StringComparison.OrdinalIgnoreCase))
                {
                    var inside = ExtractArgs(line);
                    if (inside == null || inside.Count != 3) { Console.WriteLine("Search expects 3 arguments: Search(H1, daysAhead, roomType)"); continue; }
                    var hotelId = inside[0].Trim();
                    if (!int.TryParse(inside[1].Trim(), out int daysAhead))
                    {
                        Console.WriteLine("Invalid daysAhead integer.");
                        continue;
                    }
                    var roomType = inside[2].Trim();

                    if (!hotelRoomTypeCounts.ContainsKey(hotelId))
                    {
                        Console.WriteLine($"Hotel not found: {hotelId}");
                        continue;
                    }

                    var today = DateTime.Today;
                    var horizonEnd = today.AddDays(daysAhead);
                    var perDayAvailability = new Dictionary<DateTime, int>();
                    for (var d = today; d < horizonEnd; d = d.AddDays(1))
                    {
                        int booked = 0;
                        if (bookingCalendar.TryGetValue(hotelId, out var hotelDict) && hotelDict.TryGetValue(roomType, out var roomDict) && roomDict.TryGetValue(d, out var c))
                            booked = c;
                        int total = hotelRoomTypeCounts[hotelId].TryGetValue(roomType, out var t) ? t : 0;
                        perDayAvailability[d] = total - booked;
                    }

                    var ranges = new List<(DateTime start, DateTime endInclusive, int available)>();
                    DateTime? cursorStart = null;
                    DateTime prevDay = DateTime.MinValue;
                    var currentMins = int.MaxValue;
                    foreach (var kv in perDayAvailability.OrderBy(k => k.Key))
                    {
                        var d = kv.Key;
                        var avail = kv.Value;
                        if (avail > 0)
                        {
                            if (cursorStart == null)
                            {
                                cursorStart = d;
                                currentMins = avail;
                            }
                            else
                            {
                                currentMins = Math.Min(currentMins, avail);
                            }
                            prevDay = d;
                        }
                        else
                        {
                            if (cursorStart != null)
                            {
                                ranges.Add((cursorStart.Value, prevDay, currentMins));
                                cursorStart = null;
                                currentMins = int.MaxValue;
                            }
                        }
                    }
                    if (cursorStart != null)
                    {
                        ranges.Add((cursorStart.Value, prevDay, currentMins));
                    }
                    if (ranges.Count == 0)
                    {
                        Console.WriteLine(); 
                    }
                    else
                    {
                        var outParts = ranges.Select(r => $"({r.start:yyyyMMdd}-{r.endInclusive:yyyyMMdd}, {r.available})");
                        Console.WriteLine(string.Join(", ", outParts));
                    }
                }
                else
                {
                    Console.WriteLine("Unknown command. Use Availability(...) or Search(...).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing command: {ex.Message}");
            }
        }

        Console.WriteLine("Exiting.");
    }

    static bool TryParseYmd(string s, out DateTime dt)
    {
        dt = DateTime.MinValue;
        s = s.Trim();
        if (DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return true;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return true;
        return DateTime.TryParse(s, out dt);
    }

    static List<string> ExtractArgs(string line)
    {
        var m = Regex.Match(line, @"^[^(]+\((.*)\)\s*$");
        if (!m.Success) return null;
        var inner = m.Groups[1].Value;
        var parts = new List<string>();
        int bracketDepth = 0;
        var token = "";
        foreach (var ch in inner)
        {
            if (ch == ',' && bracketDepth == 0)
            {
                parts.Add(token.Trim());
                token = "";
            }
            else
            {
                token += ch;
                if (ch == '(') bracketDepth++;
                else if (ch == ')') bracketDepth--;
            }
        }
        if (!string.IsNullOrWhiteSpace(token)) parts.Add(token.Trim());
        return parts;
    }

    static int ComputeAvailabilityForRange(
        string hotelId,
        string roomType,
        DateTime startInclusive,
        DateTime endExclusive,
        Dictionary<string, Dictionary<string, int>> hotelRoomTypeCounts,
        Dictionary<string, Dictionary<string, Dictionary<DateTime, int>>> bookingCalendar)
    {
        int total = hotelRoomTypeCounts.ContainsKey(hotelId) && hotelRoomTypeCounts[hotelId].TryGetValue(roomType, out var t) ? t : 0;
        int minAvail = int.MaxValue;
        bool anyDay = false;
        for (var d = startInclusive.Date; d < endExclusive.Date; d = d.AddDays(1))
        {
            anyDay = true;
            int booked = 0;
            if (bookingCalendar.TryGetValue(hotelId, out var hotelDict) && hotelDict.TryGetValue(roomType, out var roomDict) && roomDict.TryGetValue(d, out var c))
                booked = c;
            int avail = total - booked;
            if (avail < minAvail) minAvail = avail;
        }
        if (!anyDay) return total;
        return minAvail == int.MaxValue ? total : minAvail;
    }
}
