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

    // ==== 既存のメソッドはすべてそのまま維持 ====
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

    public void AddTemplate(string userName, TaskTemplate tmpl) {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(tmpl.TemplateName)) return;
        string targetKey = tmpl.IsShared ? "__SHARED__" : userName;
        if (!_templates.ContainsKey(targetKey)) _templates[targetKey] = new();
        var existing = _templates[targetKey].FirstOrDefault(t => t.TemplateName == tmpl.TemplateName);
        if (existing != null) _templates[targetKey].Remove(existing);
        _templates[targetKey].Add(tmpl);
        SaveTemplates();
    }

    public void UpdateTemplate(string userName, string oldName, TaskTemplate newTmpl) {
        foreach (var key in _templates.Keys.ToList()) {
            _templates[key].RemoveAll(t => t.TemplateName == oldName);
        }
        string targetKey = newTmpl.IsShared ? "__SHARED__" : userName;
        if (!_templates.ContainsKey(targetKey)) _templates[targetKey] = new();
        _templates[targetKey].Add(newTmpl);
        SaveTemplates();
    }

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
        return tasks.OrderBy(t => t.IsCompleted) 
                    .ThenByDescending(t => !string.IsNullOrEmpty(priorityCategory) && t.Category == priorityCategory) 
                    .ThenBy(t => string.IsNullOrEmpty(t.StartTime) ? "23:59" : t.StartTime); 
    }

    public Dictionary<string, List<TodoItem>> GetTodosByRange(string startDate, string endDate, string? userName, string? priorityCategory) {
        var result = new Dictionary<string, List<TodoItem>>();
        if (DateTime.TryParse(startDate, out var start) && DateTime.TryParse(endDate, out var end)) {
            for (var d = start; d <= end; d = d.AddDays(1)) {
                string dateStr = d.ToString("yyyy-MM-dd");
                var tasks = _data.TryGetValue(dateStr, out var dailyTasks) ? dailyTasks.ToList() : new List<TodoItem>();
                if (!string.IsNullOrEmpty(userName)) tasks = tasks.Where(t => t.UserName == userName).ToList();
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
                var tasks = _data.TryGetValue(dateStr, out var dailyTasks) ? dailyTasks.ToList() : new List<TodoItem>();
                
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

    public void AddBulk(List<string> dates, TodoTask task) {
        if (string.IsNullOrWhiteSpace(task.Title) || dates == null || !dates.Any()) return;

        var user = string.IsNullOrWhiteSpace(task.UserName) ? "未設定" : task.UserName;
        var cat = string.IsNullOrWhiteSpace(task.Category) ? "未分類" : task.Category;
        AddCategory(cat);
        var room = string.IsNullOrWhiteSpace(task.Room) ? "未設定" : task.Room;
        AddRoom(room); 

        int nextId = _data.Values.SelectMany(t => t).Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;

        foreach (var date in dates) {
            if (!_data.ContainsKey(date)) _data[date] = new();
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

    // ==================================================
    // 🚀 【新規追加】テンプレートの1ヶ月一括反映機能
    // ==================================================
    public void ApplyTemplateForNextMonth(string userName, string templateName, bool isShared)
    {
        // 指定されたユーザー（または共有）のテンプレを探す
        var template = GetTemplates(userName).FirstOrDefault(t => t.TemplateName == templateName && t.IsShared == isShared);
        if (template == null) return;

        // 向こう30日分の日付リストを作成
        var today = DateTime.Today;
        var dates = new List<string>();
        for (int i = 0; i < 30; i++)
        {
            dates.Add(today.AddDays(i).ToString("yyyy-MM-dd"));
        }

        // テンプレからタスクデータを作成し、既存のAddBulkで一括登録
        var task = new TodoTask(
            template.Title,
            null, // deadlineStr
            template.Category,
            template.StartTime,
            template.EndTime,
            template.Room,
            0, // actualTime
            null, // date
            userName
        );

        AddBulk(dates, task);
    }
}