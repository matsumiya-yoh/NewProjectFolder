using System.Text.Json;
using TodoBackend.Models;

namespace TodoBackend.Services;

public class TodoService {
    private const string FilePath = "todos.json";
    private const string CatFilePath = "categories.json";
    private const string RoomFilePath = "rooms.json"; 
    private const string TemplateFilePath = "templates.json"; 
    
    private readonly Dictionary<string, List<TodoItem>> _data;
    private readonly Dictionary<string, List<TaskTemplate>> _templates; 
    private List<CategoryInfo> _categories;
    private List<string> _rooms; 

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public TodoService() {
        _data = Load();
        _categories = LoadCategories();
        _rooms = LoadRooms(); 
        _templates = LoadTemplates(); 
    }

    public List<string> GetRooms() => _rooms;
    public void AddRoom(string roomName) {
        if (!string.IsNullOrWhiteSpace(roomName) && !_rooms.Contains(roomName)) {
            _rooms.Add(roomName); SaveRooms();
        }
    }
    public void DeleteRoom(string roomName) {
        if (!string.IsNullOrWhiteSpace(roomName)) {
            _rooms.Remove(roomName); SaveRooms();
        }
    }

    // 💡 変更：個人用と共有用(__SHARED__)の両方を混ぜて返す
    public List<TaskTemplate> GetTemplates(string userName) {
        var result = new List<TaskTemplate>();
        if (!string.IsNullOrWhiteSpace(userName) && _templates.TryGetValue(userName, out var userTmpls)) {
            result.AddRange(userTmpls);
        }
        if (_templates.TryGetValue("__SHARED__", out var sharedTmpls)) {
            result.AddRange(sharedTmpls.Where(s => !result.Any(r => r.TemplateName == s.TemplateName))); 
        }
        return result;
    }

    // 💡 変更：IsSharedがtrueなら __SHARED__ キーに保存する
    public void AddTemplate(string userName, TaskTemplate tmpl) {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(tmpl.TemplateName)) return;
        
        string targetKey = tmpl.IsShared ? "__SHARED__" : userName;

        if (!_templates.ContainsKey(targetKey)) _templates[targetKey] = new();
        var existing = _templates[targetKey].FirstOrDefault(t => t.TemplateName == tmpl.TemplateName);
        if (existing != null) _templates[targetKey].Remove(existing);
        
        _templates[targetKey].Add(tmpl);
        SaveTemplates();
    }

    // 💡 変更：共有・個人の区別をつけて削除
    public void DeleteTemplate(string userName, string templateName, bool isShared) {
        string targetKey = isShared ? "__SHARED__" : userName;
        if (_templates.ContainsKey(targetKey)) {
            _templates[targetKey].RemoveAll(t => t.TemplateName == templateName); 
            SaveTemplates();
        }
    }

    public List<CategoryInfo> GetCategories() => _categories;
    public void AddCategory(string categoryName) {
        if (!string.IsNullOrWhiteSpace(categoryName) && !_categories.Any(c => c.Name == categoryName)) {
            var random = new Random();
            string color = $"hsl({random.Next(0, 360)}, 70%, 50%)";
            _categories.Add(new CategoryInfo { Name = categoryName, Color = color });
            SaveCategories();
        }
    }
    public void DeleteCategory(string categoryName) {
        if (!string.IsNullOrWhiteSpace(categoryName)) {
            _categories.RemoveAll(c => c.Name == categoryName); SaveCategories();
        }
    }

    private IEnumerable<TodoItem> SortTasks(IEnumerable<TodoItem> tasks, string? priorityCategory) {
        return tasks
            .OrderBy(t => t.IsCompleted) 
            .ThenByDescending(t => !string.IsNullOrEmpty(priorityCategory) && t.Category == priorityCategory) 
            .ThenBy(t => string.IsNullOrEmpty(t.StartTime) ? "23:59" : t.StartTime); 
    }

    public Dictionary<string, List<TodoItem>> GetTodosByRange(string startDate, string endDate, string? userName, string? priorityCategory) {
        var result = new Dictionary<string, List<TodoItem>>();
        if (DateTime.TryParse(startDate, out var start) && DateTime.TryParse(endDate, out var end)) {
            for (var d = start; d <= end; d = d.AddDays(1)) {
                string dateStr = d.ToString("yyyy-MM-dd");
                var tasks = _data.TryGetValue(dateStr, out var dailyTasks) ? dailyTasks : new List<TodoItem>();
                if (!string.IsNullOrEmpty(userName)) {
                    tasks = tasks.Where(t => t.UserName == userName).ToList();
                }
                result[dateStr] = SortTasks(tasks, priorityCategory).ToList();
            }
        }
        return result;
    }

    public Dictionary<string, Dictionary<string, List<TodoItem>>> GetCompanyMatrix(string startDate, string endDate, List<string> teamMembers, string? priorityCategory) {
        var result = new Dictionary<string, Dictionary<string, List<TodoItem>>>();
        foreach (var member in teamMembers) { result[member] = new Dictionary<string, List<TodoItem>>(); }

        if (DateTime.TryParse(startDate, out var start) && DateTime.TryParse(endDate, out var end)) {
            for (var d = start; d <= end; d = d.AddDays(1)) {
                string dateStr = d.ToString("yyyy-MM-dd");
                var tasks = _data.TryGetValue(dateStr, out var dailyTasks) ? dailyTasks : new List<TodoItem>();
                
                foreach (var member in teamMembers) {
                    result[member][dateStr] = SortTasks(tasks.Where(t => t.UserName == member), priorityCategory).ToList();
                }
                
                var otherUsers = tasks.Select(t => t.UserName).Where(u => !string.IsNullOrEmpty(u) && !teamMembers.Contains(u)).Distinct();
                foreach(var otherUser in otherUsers) {
                    if (!result.ContainsKey(otherUser!)) result[otherUser!] = new Dictionary<string, List<TodoItem>>();
                    result[otherUser!][dateStr] = SortTasks(tasks.Where(t => t.UserName == otherUser), priorityCategory).ToList();
                }
            }
        }
        return result;
    }

    public object GetTimesheet(List<string> dates) {
        var report = new Dictionary<string, Dictionary<string, int>>();
        var companyReport = new Dictionary<string, Dictionary<string, int>>();

        foreach (var cat in _categories) {
            report[cat.Name] = new Dictionary<string, int>();
            companyReport[cat.Name] = new Dictionary<string, int>();

            foreach (var date in dates) {
                if (_data.TryGetValue(date, out var tasks)) {
                    int totalActual = tasks.Where(t => t.Category == cat.Name).Sum(t => t.ActualTime);
                    companyReport[cat.Name][date] = totalActual;
                    report[cat.Name][date] = totalActual; 
                } else {
                    report[cat.Name][date] = 0; companyReport[cat.Name][date] = 0;
                }
            }
        }
        return new { dates, report, companyReport };
    }

    public string GenerateTimesheetCsv(string startDate, string endDate, string viewMode, string? userName) {
        var dates = new List<string>();
        if (DateTime.TryParse(startDate, out var start) && DateTime.TryParse(endDate, out var end)) {
            for (var d = start; d <= end; d = d.AddDays(1)) dates.Add(d.ToString("yyyy-MM-dd"));
        }

        var report = new Dictionary<string, Dictionary<string, int>>();
        foreach (var cat in _categories) {
            report[cat.Name] = new Dictionary<string, int>();
            foreach (var date in dates) {
                report[cat.Name][date] = 0;
                if (_data.TryGetValue(date, out var tasks)) {
                    var targetTasks = viewMode == "company" ? tasks : tasks.Where(t => t.UserName == userName);
                    report[cat.Name][date] = targetTasks.Where(t => t.Category == cat.Name).Sum(t => t.ActualTime);
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("案件名," + string.Join(",", dates.Select(d => d.Substring(5))) + ",案件別合計");

        int[] dailyGrandTotals = new int[dates.Count];
        foreach (var cat in _categories) {
            int rowTotal = 0;
            var rowValues = new List<string>();
            for (int i = 0; i < dates.Count; i++) {
                int mins = report[cat.Name][dates[i]];
                rowTotal += mins;
                dailyGrandTotals[i] += mins;
                rowValues.Add((mins / 60.0).ToString("0.##"));
            }
            sb.AppendLine($"{cat.Name},{string.Join(",", rowValues)},{(rowTotal / 60.0).ToString("0.##")}");
        }

        var footerValues = dailyGrandTotals.Select(mins => (mins / 60.0).ToString("0.##"));
        double grandTotal = dailyGrandTotals.Sum() / 60.0;
        sb.AppendLine($"総合計,{string.Join(",", footerValues)},{grandTotal.ToString("0.##")}");

        return sb.ToString();
    }

    public List<TodoItem> GetAllTodos() => _data.Values.SelectMany(x => x).ToList();

    public List<TodoItem> GetTodos(string date, string? query, bool hideCompleted) {
        if (!_data.TryGetValue(date, out var tasks)) return new();
        var result = tasks.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query)) result = result.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (hideCompleted) result = result.Where(t => !t.IsCompleted);
        return result.ToList();
    }

    // 💡 追加：複数日付への一括登録処理
    public void AddBulk(List<string> dates, TodoTask task) {
        if (string.IsNullOrWhiteSpace(task.Title) || dates == null || !dates.Any()) return;

        var user = string.IsNullOrWhiteSpace(task.UserName) ? "未設定" : task.UserName;
        var cat = string.IsNullOrWhiteSpace(task.Category) ? "未分類" : task.Category;
        AddCategory(cat);
        var room = string.IsNullOrWhiteSpace(task.Room) ? "未設定" : task.Room;
        AddRoom(room); 

        // 全タスクのIDの最大値を取得（一括登録時にインクリメントする）
        int nextId = _data.Values.SelectMany(t => t).Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;

        foreach (var date in dates) {
            if (!_data.ContainsKey(date)) _data[date] = new();
            // その日の重複チェック
            if (_data[date].Any(t => t.Title == task.Title && t.StartTime == task.StartTime && t.EndTime == task.EndTime && t.UserName == user && !t.IsCompleted)) continue;

            DateTime? deadline = null;
            if (!string.IsNullOrEmpty(task.DeadlineStr) && DateTime.TryParse($"{date} {task.DeadlineStr}", out var parsed)) deadline = parsed;

            var newItem = new TodoItem(task.Title, false, deadline, cat, task.StartTime, task.EndTime, room, task.ActualTime, date, user) { Id = nextId++ };
            _data[date].Add(newItem);
        }
        Save();
    }

    public void Add(string date, TodoTask task) {
        if (string.IsNullOrWhiteSpace(task.Title)) return;
        if (!_data.ContainsKey(date)) _data[date] = [];

        var user = string.IsNullOrWhiteSpace(task.UserName) ? "未設定" : task.UserName;
        if (_data[date].Any(t => t.Title == task.Title && t.StartTime == task.StartTime && t.EndTime == task.EndTime && t.UserName == user && !t.IsCompleted)) return;

        DateTime? deadline = null;
        if (!string.IsNullOrEmpty(task.DeadlineStr) && DateTime.TryParse($"{date} {task.DeadlineStr}", out var parsed)) deadline = parsed;

        var cat = string.IsNullOrWhiteSpace(task.Category) ? "未分類" : task.Category;
        AddCategory(cat);
        var room = string.IsNullOrWhiteSpace(task.Room) ? "未設定" : task.Room;
        AddRoom(room); 

        int nextId = _data.Values.SelectMany(t => t).Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
        var newItem = new TodoItem(task.Title, false, deadline, cat, task.StartTime, task.EndTime, room, task.ActualTime, date, user) { Id = nextId };
        _data[date].Add(newItem);
        Save();
    }

    public void UpdateTask(string date, int id, TaskUpdateRequest req) {
        if (!_data.TryGetValue(date, out var tasks)) return;
        var targetTask = tasks.FirstOrDefault(t => t.Id == id);
        if (targetTask == null) return;

        var originalTitle = targetTask.Title;
        var originalCategory = targetTask.Category;
        
        var relatedTasks = tasks.Where(t => t.Title == originalTitle && t.Category == originalCategory).ToList();
        var currentMembers = relatedTasks.Select(t => t.UserName).ToList();
        var newMembers = req.Members ?? new List<string>();

        var removedMembers = currentMembers.Except(newMembers).ToList();
        tasks.RemoveAll(t => t.Title == originalTitle && t.Category == originalCategory && removedMembers.Contains(t.UserName));

        var keptMembers = currentMembers.Intersect(newMembers).ToList();
        foreach (var t in tasks.Where(t => t.Title == originalTitle && t.Category == originalCategory && keptMembers.Contains(t.UserName))) {
            t.Title = req.Title; t.Category = req.Category; t.StartTime = req.StartTime ?? ""; t.EndTime = req.EndTime ?? ""; t.Room = req.Room ?? "未設定"; t.ActualTime = req.ActualTime;
        }

        var addedMembers = newMembers.Except(currentMembers).ToList();
        AddCategory(req.Category);
        var room = string.IsNullOrWhiteSpace(req.Room) ? "未設定" : req.Room;
        AddRoom(room);

        int nextId = GetAllTodos().Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
        foreach (var user in addedMembers) {
            var newItem = new TodoItem(req.Title, targetTask.IsCompleted, targetTask.Deadline, req.Category, req.StartTime, req.EndTime, room, req.ActualTime, date, user) { Id = nextId++ };
            tasks.Add(newItem);
        }
        Save();
    }

    public void UpdateTime(string date, int id, string? startTime, string? endTime, int actTime) {
        if (_data.TryGetValue(date, out var tasks)) {
            var targetTask = tasks.FirstOrDefault(t => t.Id == id);
            if (targetTask == null) return;
            var relatedTasks = tasks.Where(t => t.Title == targetTask.Title && t.Category == targetTask.Category).ToList();
            foreach (var t in relatedTasks) { 
                if (startTime != null) t.StartTime = startTime; 
                if (endTime != null) t.EndTime = endTime; 
                t.ActualTime = actTime; 
            }
            Save();
        }
    }

    public void Toggle(string date, int id) {
        if (_data.TryGetValue(date, out var tasks)) {
            var targetTask = tasks.FirstOrDefault(t => t.Id == id);
            if (targetTask == null) return;
            bool newState = !targetTask.IsCompleted;
            var relatedTasks = tasks.Where(t => t.Title == targetTask.Title && t.Category == targetTask.Category).ToList();
            foreach (var t in relatedTasks) { t.IsCompleted = newState; }
            Save();
        }
    }

    public void Delete(string date, int id) {
        if (_data.TryGetValue(date, out var tasks)) {
            var targetTask = tasks.FirstOrDefault(t => t.Id == id);
            if (targetTask == null) return;
            tasks.RemoveAll(t => t.Title == targetTask.Title && t.Category == targetTask.Category);
            Save();
        }
    }

    public void CarryOver(string date, int id) {
        if (_data.TryGetValue(date, out var tasks)) {
            var targetTask = tasks.FirstOrDefault(t => t.Id == id);
            if (targetTask == null) return;
            if (DateTime.TryParse(date, out var parsedDate)) {
                string nextDateStr = parsedDate.AddDays(1).ToString("yyyy-MM-dd");
                var relatedTasks = tasks.Where(t => t.Title == targetTask.Title && t.Category == targetTask.Category).ToList();
                foreach (var t in relatedTasks) {
                    Add(nextDateStr, new TodoTask(t.Title, null, t.Category, t.StartTime, t.EndTime, t.Room, 0, nextDateStr, t.UserName));
                }
            }
        }
    }

    public object GetStats(string date, string? userName = null) {
        var defaultBreakdown = new Dictionary<string, int>();
        if (!_data.TryGetValue(date, out var tasks) || tasks.Count == 0)
            return new { progress = 0, emoji = "😴", streakCount = CalculateStreak(date, userName), timeBreakdown = defaultBreakdown, remainingCount = 0, totalTimeFormatted = "0分" };

        var targetTasks = string.IsNullOrEmpty(userName) ? tasks : tasks.Where(t => t.UserName == userName).ToList();
        if (targetTasks.Count == 0)
            return new { progress = 0, emoji = "😴", streakCount = CalculateStreak(date, userName), timeBreakdown = defaultBreakdown, remainingCount = 0, totalTimeFormatted = "0分" };

        int total = targetTasks.Count;
        int completedCount = targetTasks.Count(t => t.IsCompleted);
        int progress = total > 0 ? (int)Math.Round((double)completedCount / total * 100) : 0;
        int remaining = total - completedCount;

        string emoji = progress switch { 100 => "🤩", >= 80 => "😊", >= 50 => "😐", > 0 => "💦", _ => "😴" };

        var timeBreakdown = targetTasks.Where(t => t.ActualTime > 0)
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "未分類" : t.Category)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.ActualTime));

        int totalMins = timeBreakdown.Values.Sum();
        string formattedTime = totalMins / 60 > 0 ? $"{totalMins / 60}時間 {totalMins % 60}分" : $"{totalMins % 60}分";

        return new { progress = progress, emoji = emoji, streakCount = CalculateStreak(date, userName), timeBreakdown = timeBreakdown, remainingCount = remaining, totalTimeFormatted = formattedTime };
    }

    public Dictionary<string, object> GetStatsByRange(string startDate, string endDate, string? userName = null) {
        var result = new Dictionary<string, object>();
        if (DateTime.TryParse(startDate, out var start) && DateTime.TryParse(endDate, out var end)) {
            for (var d = start; d <= end; d = d.AddDays(1)) {
                string dateStr = d.ToString("yyyy-MM-dd");
                result[dateStr] = GetStats(dateStr, userName);
            }
        }
        return result;
    }

    public object GetGlobalInsights() {
        var allTasks = _data.Values.SelectMany(x => x).ToList();
        if (allTasks.Count == 0) return new { totalCompleted = 0, globalCompletionRate = 0, favoriteCategory = "-" };
        var completedTasks = allTasks.Where(t => t.IsCompleted).ToList();
        var favCategory = allTasks.Where(t => t.ActualTime > 0)
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "未分類" : t.Category)
            .OrderByDescending(g => g.Sum(t => t.ActualTime)).Select(g => g.Key).FirstOrDefault() ?? "-";
        return new {
            totalCompleted = completedTasks.Count,
            globalCompletionRate = (int)Math.Round((double)completedTasks.Count / allTasks.Count * 100),
            favoriteCategory = favCategory
        };
    }

    private int CalculateStreak(string date, string? userName) {
        if (!DateTime.TryParse(date, out var currentDt)) return 0;
        int streak = 0; var checkDate = currentDt;
        if (!HasCompletedTasks(checkDate, userName)) {
            checkDate = checkDate.AddDays(-1);
            if (!HasCompletedTasks(checkDate, userName)) return 0;
        }
        while (HasCompletedTasks(checkDate, userName)) {
            streak++; checkDate = checkDate.AddDays(-1);
        }
        return streak;
    }

    private bool HasCompletedTasks(DateTime dt, string? userName) {
        if (_data.TryGetValue(dt.ToString("yyyy-MM-dd"), out var dailyTasks)) {
            var targetTasks = string.IsNullOrEmpty(userName) ? dailyTasks : dailyTasks.Where(t => t.UserName == userName);
            return targetTasks.Any(t => t.IsCompleted);
        }
        return false;
    }

    private void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, _options));
    private void SaveCategories() => File.WriteAllText(CatFilePath, JsonSerializer.Serialize(_categories, _options));
    private void SaveRooms() => File.WriteAllText(RoomFilePath, JsonSerializer.Serialize(_rooms, _options)); 
    private void SaveTemplates() => File.WriteAllText(TemplateFilePath, JsonSerializer.Serialize(_templates, _options)); 

    private Dictionary<string, List<TodoItem>> Load() {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, List<TodoItem>>>(File.ReadAllText(FilePath)) ?? new() : new(); } 
        catch { return new(); }
    }

    private List<string> LoadRooms() {
        try { return File.Exists(RoomFilePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RoomFilePath)) ?? new() : new(); } 
        catch { return new(); }
    }

    private Dictionary<string, List<TaskTemplate>> LoadTemplates() {
        try { return File.Exists(TemplateFilePath) ? JsonSerializer.Deserialize<Dictionary<string, List<TaskTemplate>>>(File.ReadAllText(TemplateFilePath)) ?? new() : new(); } 
        catch { return new(); }
    }

    private List<CategoryInfo> LoadCategories() {
        try { 
            if (File.Exists(CatFilePath)) {
                var text = File.ReadAllText(CatFilePath);
                try {
                    var cats = JsonSerializer.Deserialize<List<CategoryInfo>>(text);
                    if (cats != null && cats.Any()) return cats;
                } catch {
                    var oldCats = JsonSerializer.Deserialize<List<string>>(text);
                    if (oldCats != null) {
                        var random = new Random();
                        return oldCats.Select(c => new CategoryInfo { Name = c, Color = $"hsl({random.Next(0, 360)}, 70%, 50%)" }).ToList();
                    }
                }
            }
        } catch {}
        return new List<CategoryInfo> { new CategoryInfo { Name = "開発", Color = "hsl(210, 70%, 50%)" }, new CategoryInfo { Name = "会議", Color = "hsl(330, 70%, 50%)" } };
    }
}