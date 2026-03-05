using System.Text.Json;
using TodoBackend.Models;

namespace TodoBackend.Services;

public class TodoService {
    private const string FilePath = "todos.json";
    private const string CatFilePath = "categories.json";
    private const string GroupFilePath = "groups.json"; 
    private const string RoomFilePath = "rooms.json"; // 💡 新規：部屋リスト保存用
    
    private readonly Dictionary<string, List<TodoItem>> _data;
    private List<CategoryInfo> _categories;
    private readonly Dictionary<string, List<UserGroup>> _userGroups;
    private List<string> _rooms; // 💡 新規：部屋のリスト

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public TodoService() {
        _data = Load();
        _categories = LoadCategories();
        _userGroups = LoadGroups();
        _rooms = LoadRooms(); // 💡 部屋の読み込み
    }

    // ==========================================
    // 💡 要件：部屋（Room）の管理
    // ==========================================
    public List<string> GetRooms() => _rooms;
    public void AddRoom(string roomName) {
        if (!string.IsNullOrWhiteSpace(roomName) && !_rooms.Contains(roomName)) {
            _rooms.Add(roomName);
            SaveRooms();
        }
    }
    public void DeleteRoom(string roomName) {
        if (!string.IsNullOrWhiteSpace(roomName)) {
            _rooms.Remove(roomName);
            SaveRooms();
        }
    }

    // ==========================================
    // グループ管理
    // ==========================================
    public List<UserGroup> GetUserGroups(string userName) {
        if (string.IsNullOrWhiteSpace(userName)) return new();
        return _userGroups.GetValueOrDefault(userName) ?? new();
    }
    public void AddUserGroup(string userName, UserGroup group) {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(group.GroupName)) return;
        if (!_userGroups.ContainsKey(userName)) _userGroups[userName] = new();
        var existing = _userGroups[userName].FirstOrDefault(g => g.GroupName == group.GroupName);
        if (existing != null) existing.Members = group.Members;
        else _userGroups[userName].Add(group);
        SaveGroups();
    }
    public void DeleteUserGroup(string userName, string groupName) {
        if (_userGroups.ContainsKey(userName)) {
            _userGroups[userName].RemoveAll(g => g.GroupName == groupName);
            SaveGroups();
        }
    }

    // ==========================================
    // カテゴリー管理
    // ==========================================
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
            _categories.RemoveAll(c => c.Name == categoryName);
            SaveCategories();
        }
    }

    // ==========================================
    // タイムシート・データ取得
    // ==========================================
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

    public List<TodoItem> GetAllTodos() => _data.Values.SelectMany(x => x).ToList();

    public List<TodoItem> GetTodos(string date, string? query, bool hideCompleted) {
        if (!_data.TryGetValue(date, out var tasks)) return new();
        var result = tasks.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query)) result = result.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (hideCompleted) result = result.Where(t => !t.IsCompleted);
        return result.ToList();
    }

    // ==========================================
    // タスク操作（追加・変更・削除）
    // ==========================================
    public void Add(string date, TodoTask task) {
        if (string.IsNullOrWhiteSpace(task.Title)) return;
        if (!_data.ContainsKey(date)) _data[date] = [];

        var user = string.IsNullOrWhiteSpace(task.UserName) ? "未設定" : task.UserName;
        
        // 💡 変更：重複判定の条件からEstimatedTimeを削除し、時間と部屋を追加
        if (_data[date].Any(t => t.Title == task.Title && t.StartTime == task.StartTime && t.EndTime == task.EndTime && t.UserName == user && !t.IsCompleted)) return;

        DateTime? deadline = null;
        if (!string.IsNullOrEmpty(task.DeadlineStr) && DateTime.TryParse($"{date} {task.DeadlineStr}", out var parsed)) deadline = parsed;

        var cat = string.IsNullOrWhiteSpace(task.Category) ? "未分類" : task.Category;
        AddCategory(cat);
        
        var room = string.IsNullOrWhiteSpace(task.Room) ? "未設定" : task.Room;
        AddRoom(room); // 新しい部屋が来たら自動登録

        int nextId = _data.Values.SelectMany(t => t).Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
        var newItem = new TodoItem(task.Title, false, deadline, cat, task.StartTime, task.EndTime, room, task.ActualTime, date, user) { Id = nextId };
        _data[date].Add(newItem);
        Save();
    }

    // 💡 変更：タスク名、時間、部屋、メンバーを一括更新
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
            t.Title = req.Title;
            t.Category = req.Category;
            t.StartTime = req.StartTime ?? ""; // 💡 追加
            t.EndTime = req.EndTime ?? "";     // 💡 追加
            t.Room = req.Room ?? "未設定";       // 💡 追加
            t.ActualTime = req.ActualTime;
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

    // 💡 変更：時間と部屋のみの部分更新
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

    // 💡 変更：持ち越し時にStartTime/EndTimeとRoomをコピー
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

    // ==========================================
    // 統計計算
    // ==========================================
    public object GetStats(string date, string? userName = null) {
        if (!_data.TryGetValue(date, out var tasks) || tasks.Count == 0)
            return new { progress = 0, emoji = "😴", streakCount = CalculateStreak(date, userName), timeBreakdown = new Dictionary<string, int>() };

        var targetTasks = string.IsNullOrEmpty(userName) ? tasks : tasks.Where(t => t.UserName == userName).ToList();
        if (targetTasks.Count == 0)
            return new { progress = 0, emoji = "😴", streakCount = CalculateStreak(date, userName), timeBreakdown = new Dictionary<string, int>() };

        int total = targetTasks.Count;
        int completedCount = targetTasks.Count(t => t.IsCompleted);
        int progress = (int)Math.Round((double)completedCount / total * 100);

        string emoji = progress switch { 100 => "🤩", >= 80 => "😊", >= 50 => "😐", > 0 => "💦", _ => "😴" };

        var timeBreakdown = targetTasks.Where(t => t.ActualTime > 0)
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "未分類" : t.Category)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.ActualTime));

        return new { progress = progress, emoji = emoji, streakCount = CalculateStreak(date, userName), timeBreakdown = timeBreakdown };
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

    // ==========================================
    // データ保存・読み込み
    // ==========================================
    private void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, _options));
    private void SaveCategories() => File.WriteAllText(CatFilePath, JsonSerializer.Serialize(_categories, _options));
    private void SaveGroups() => File.WriteAllText(GroupFilePath, JsonSerializer.Serialize(_userGroups, _options));
    private void SaveRooms() => File.WriteAllText(RoomFilePath, JsonSerializer.Serialize(_rooms, _options)); // 💡 追加

    private Dictionary<string, List<TodoItem>> Load() {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, List<TodoItem>>>(File.ReadAllText(FilePath)) ?? new() : new(); } 
        catch { return new(); }
    }

    private Dictionary<string, List<UserGroup>> LoadGroups() {
        try { return File.Exists(GroupFilePath) ? JsonSerializer.Deserialize<Dictionary<string, List<UserGroup>>>(File.ReadAllText(GroupFilePath)) ?? new() : new(); } 
        catch { return new(); }
    }

    private List<string> LoadRooms() {
        try { return File.Exists(RoomFilePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RoomFilePath)) ?? new() : new(); } 
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