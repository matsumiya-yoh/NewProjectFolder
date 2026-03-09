import { Component, OnInit, ChangeDetectorRef, ChangeDetectionStrategy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpClientModule } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { DragDropModule } from '@angular/cdk/drag-drop'; 

export interface TodoItem { 
  id?: number;            
  title: string;          
  isCompleted: boolean;   
  category: string;       
  actualTime: number;     
  deadline?: string;      
  date?: string;          
  userName?: string;      
  startTime?: string;      
  endTime?: string;        
  room?: string;          
}

// 💡 変更：バックエンドで計算された remainingCount と totalTimeFormatted を受け取る
export interface TodoStats { 
  progress: number;       
  emoji: string;          
  streakCount: number;    
  timeBreakdown: { [key: string]: number }; 
  remainingCount: number;
  totalTimeFormatted: string;
}

export interface GlobalInsights { 
  totalCompleted: number;       
  globalCompletionRate: number; 
  favoriteCategory: string;     
}

export interface CategoryInfo {
  name: string;
  color: string;
}

export interface EditingTaskData {
  title: string;
  category: string;
  startTime: string;
  endTime: string;
  room: string;
  actTime: number;
  members: string[];
}

export interface TaskTemplate {
  templateName: string;
  title: string;
  category: string;
  room: string;
  startTime: string;
  endTime: string;
  members: string[];
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, HttpClientModule, FormsModule, DragDropModule], 
  templateUrl: './app.html', 
  styleUrls: ['./app.css'],
  changeDetection: ChangeDetectionStrategy.OnPush 
})
export class AppComponent implements OnInit {

  viewMode: 'personal' | 'company' = 'personal';

  teamMembers: string[] = [];      
  currentUser: string = '';        
  isUserDropdownOpen = false;      

  user: string = ''; 

  // 💡 変更：dailyDataを廃止し、個人用と全体用の配列に直接格納
  matrixData: { [user: string]: { [date: string]: TodoItem[] } } = {}; 
  personalData: { [date: string]: TodoItem[] } = {}; 

  dateRange: string[] = [];                       
  selectedDate: string = new Date().toISOString().split('T')[0]; 
  today: string = new Date().toISOString().split('T')[0];

  dailyStats: { [date: string]: TodoStats } = {}; 
  stats: TodoStats = { progress: 0, emoji: '😴', streakCount: 0, timeBreakdown: {}, remainingCount: 0, totalTimeFormatted: '0分' }; 
  insights: GlobalInsights = { totalCompleted: 0, globalCompletionRate: 0, favoriteCategory: '-' }; 

  editingCategories: { [key: string]: string } = {}; 
  filterCategories: { [date: string]: string } = {}; 
  categories: CategoryInfo[] = [];                         
  rooms: string[] = [];
  editingRooms: { [key: string]: string } = {};

  tsDates: string[] = [];                             
  timesheet: { dates: string[], report: any, companyReport: any } | null = null; 

  hoveredCardId: string | null = null;
  lastScrollLeft = 0; 
  isCalendarOpen = false;
  currentMonth: Date = new Date();
  calendarWeeks: Date[][] = [];

  coWorkers: { [key: string]: string[] } = {};
  savedTemplates: TaskTemplate[] = []; 

  editingTaskId: number | null = null;
  editingTaskData: EditingTaskData = { title: '', category: '', startTime: '', endTime: '', room: '', actTime: 0, members: [] };
  
  errorMessage: string | null = null; 
  exportStartDate: string = '';
  exportEndDate: string = '';

  activeTemplateKey: string | null = null;
  isPopupHovered = false;    
  isTemplateHovered = false; 

  private isAdding = false; 
  private scrollTimeout: any = null; 
  private tsScrollTimer: any = null; 
  private readonly apiUrl = 'http://localhost:5099/api/todo';        
  private readonly insightsUrl = 'http://localhost:5099/api/insights'; 

  constructor(private http: HttpClient, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void {
    this.loadMembers();                  
    this.initTsDates();                  
    this.fetchCategories();              
    this.fetchRooms(); 
    this.refreshView(this.selectedDate); 
    this.fetchGlobalInsights();          
    this.initExportDates();             
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    this.closeAllPopups();
  }

  closeAllPopups(): void {
    this.activeTemplateKey = null;
    this.isUserDropdownOpen = false;
    this.isCalendarOpen = false;
    this.cdr.detectChanges();
  }

  toggleTemplateMenu(key: string, event: Event): void {
    event.stopPropagation(); 
    this.activeTemplateKey = this.activeTemplateKey === key ? null : key;
    this.cdr.detectChanges();
  }

  loadMembers(): void {
    const saved = localStorage.getItem('teamMembers');
    this.teamMembers = saved ? JSON.parse(saved) : ['自分', 'Aさん', 'Bさん'];
    this.currentUser = this.teamMembers[0];
    this.fetchTemplates(); 
  }

  addNewUser(name: string): void {
    const trimmed = name.trim();
    if (!trimmed || this.teamMembers.includes(trimmed)) return; 
    this.teamMembers.push(trimmed);
    localStorage.setItem('teamMembers', JSON.stringify(this.teamMembers));
    this.refreshView(this.selectedDate); // バックエンドから再取得
    this.selectAccountTarget(trimmed);
  }

  addNewMemberOnly(name: string): void {
    const trimmed = name.trim();
    if (!trimmed || this.teamMembers.includes(trimmed)) return; 
    this.teamMembers.push(trimmed);
    localStorage.setItem('teamMembers', JSON.stringify(this.teamMembers));
    this.refreshView(this.selectedDate);
    this.cdr.detectChanges();
  }

  selectAccountTarget(target: string): void {
    this.isUserDropdownOpen = false;
    if (target === 'company') this.viewMode = 'company';
    else { 
      this.viewMode = 'personal'; 
      this.currentUser = target; 
      this.fetchTemplates(); 
    }
    this.refreshView(this.selectedDate); 
    this.fetchGlobalInsights();
    this.cdr.detectChanges(); 
  }

  openUserDropdown(): void { this.closeAllPopups(); this.isUserDropdownOpen = true; this.cdr.detectChanges(); }
  closeUserDropdown(): void { this.isUserDropdownOpen = false; this.cdr.detectChanges(); }
  openCalendar(): void { this.closeAllPopups(); this.isCalendarOpen = true; this.currentMonth = new Date(this.selectedDate); this.generateCalendar(); this.cdr.detectChanges(); }
  closeCalendar(): void { this.isCalendarOpen = false; this.cdr.detectChanges(); }

  changeMonth(offset: number, event: Event): void {
    event.stopPropagation();
    this.currentMonth.setMonth(this.currentMonth.getMonth() + offset);
    this.generateCalendar();
    this.cdr.detectChanges();
  }

  generateCalendar(): void {
    const year = this.currentMonth.getFullYear(); const month = this.currentMonth.getMonth();
    const firstDay = new Date(year, month, 1); const lastDay = new Date(year, month + 1, 0);
    let startDate = new Date(firstDay); startDate.setDate(startDate.getDate() - startDate.getDay()); 
    const weeks: Date[][] = []; let currentWeek: Date[] = []; let iterDate = new Date(startDate);
    while (iterDate <= lastDay || currentWeek.length > 0) {
      currentWeek.push(new Date(iterDate)); iterDate.setDate(iterDate.getDate() + 1);
      if (currentWeek.length === 7) { weeks.push(currentWeek); currentWeek = []; }
    }
    this.calendarWeeks = weeks;

    weeks.forEach(w => w.forEach(d => {
      const dStr = this.formatDate(d);
      if (!this.dailyStats[dStr]) {
        const u = this.viewMode === 'personal' ? this.currentUser : '';
        this.http.get<TodoStats>(`${this.apiUrl}/${dStr}/stats?userName=${encodeURIComponent(u)}`).subscribe(res => {
          this.dailyStats[dStr] = res; this.cdr.detectChanges();
        });
      }
    }));
  }

  formatDate(d: Date): string { return `${d.getFullYear()}-${('0' + (d.getMonth() + 1)).slice(-2)}-${('0' + d.getDate()).slice(-2)}`; }

  selectDateFromCalendar(d: Date): void {
    this.selectedDate = this.formatDate(d);
    this.isCalendarOpen = false; 
    this.refreshView(this.selectedDate); 
    this.initTsDates();   
    this.fetchTimesheet();
    this.cdr.detectChanges();
    setTimeout(() => { document.getElementById('date-col-' + this.selectedDate)?.scrollIntoView({ behavior: 'smooth', inline: 'center' }); }, 100);
  }

  shiftWeek(offset: number): void {
    const d = new Date(this.selectedDate); d.setDate(d.getDate() + (offset * 7)); this.selectDateFromCalendar(d); 
  }

  // ==========================================
  // インサイト・統計
  // ==========================================
  fetchGlobalInsights(): void {
    this.http.get<GlobalInsights>(this.insightsUrl).subscribe(data => {
      this.insights = data; this.cdr.detectChanges();
    });
  }

  // 💡 変更：dailyData廃止に伴い、personalData/matrixDataから算出
  get todayTotal(): number { 
    if (this.viewMode === 'personal') return this.personalData[this.today]?.length || 0;
    return Object.values(this.matrixData).reduce((sum, uData) => sum + (uData[this.today]?.length || 0), 0);
  }
  get todayCompleted(): number { 
    if (this.viewMode === 'personal') return this.personalData[this.today]?.filter(t=>t.isCompleted).length || 0;
    return Object.values(this.matrixData).reduce((sum, uData) => sum + (uData[this.today]?.filter(t=>t.isCompleted).length || 0), 0);
  }

  // ==========================================
  // 案件・部屋管理
  // ==========================================
  fetchCategories(): void {
    this.http.get<CategoryInfo[]>('http://localhost:5099/api/categories').subscribe(cats => {
      this.categories = cats; 
      Object.keys(this.editingCategories).forEach(d => {
        if (!this.categories.some(c => c.name === this.editingCategories[d])) {
          this.editingCategories[d] = this.categories.length > 0 ? this.categories[0].name : '未分類';
        }
      });
      this.fetchTimesheet(); this.cdr.detectChanges();
    });
  }

  addCustomCategory(name: string, date: string, user: string): void {
    if (!name.trim()) return; 
    this.http.post(`http://localhost:5099/api/categories/${encodeURIComponent(name.trim())}`, {}).subscribe(() => {
      this.fetchCategories(); if (date && user) this.setEditingCategory(date, user, name.trim()); 
    });
  }

  deleteCategory(catName: string, event: Event): void {
    event.stopPropagation(); 
    this.http.delete(`http://localhost:5099/api/categories/${encodeURIComponent(catName)}`).subscribe(() => {
      this.fetchCategories(); this.fetchTimesheet();  
    });
  }

  getEditingCategory(date: string, user: string): string { return this.editingCategories[`${date}-${user}`] || (this.categories.length > 0 ? this.categories[0].name : '未分類'); }
  setEditingCategory(date: string, user: string, cat: string): void { this.editingCategories[`${date}-${user}`] = cat; }

  fetchRooms(): void {
    this.http.get<string[]>('http://localhost:5099/api/rooms').subscribe(data => {
      this.rooms = data; 
      Object.keys(this.editingRooms).forEach(d => {
        if (!this.rooms.includes(this.editingRooms[d])) {
          this.editingRooms[d] = this.rooms.length > 0 ? this.rooms[0] : '未設定';
        }
      });
      this.cdr.detectChanges();
    });
  }

  addCustomRoom(name: string, date: string, user: string): void {
    if (!name.trim()) return; 
    this.http.post(`http://localhost:5099/api/rooms/${encodeURIComponent(name.trim())}`, {}).subscribe(() => {
      this.fetchRooms(); if (date && user) this.setEditingRoom(date, user, name.trim()); 
    });
  }

  deleteRoom(roomName: string, event: Event): void {
    event.stopPropagation(); 
    this.http.delete(`http://localhost:5099/api/rooms/${encodeURIComponent(roomName)}`).subscribe(() => {
      this.fetchRooms(); 
    });
  }

  getEditingRoom(date: string, user: string): string { return this.editingRooms[`${date}-${user}`] || '未設定'; }
  setEditingRoom(date: string, user: string, room: string): void { this.editingRooms[`${date}-${user}`] = room; }

  // ==========================================
  // タスクテンプレート管理
  // ==========================================
  fetchTemplates(): void {
    if (!this.currentUser) return;
    this.http.get<TaskTemplate[]>(`http://localhost:5099/api/templates/${encodeURIComponent(this.currentUser)}`).subscribe({
      next: (data) => {
        this.savedTemplates = data || [];
        this.cdr.detectChanges();
      },
      error: (err) => this.handleError('テンプレの読み込みに失敗しました', err)
    });
  }

  saveTemplate(templateName: string, date: string, user: string, title: string, start: string, end: string): void {
    const finalName = templateName.trim() || title.trim();
    if (!finalName) {
      this.handleError('テンプレ名、またはタスク名を入力してください', null);
      return;
    }
    if (!this.currentUser) return;
    
    const payload: TaskTemplate = { 
      templateName: finalName, 
      title: title,
      category: this.getEditingCategory(date, user),
      room: this.getEditingRoom(date, user),
      startTime: start,
      endTime: end,
      members: this.coWorkers[`${date}-${user}`] || [] 
    };
    
    this.http.post(`http://localhost:5099/api/templates/${encodeURIComponent(this.currentUser)}`, payload).subscribe({
      next: () => {
        this.fetchTemplates();
        this.activeTemplateKey = null; 
        this.cdr.detectChanges();
      },
      error: (err) => this.handleError('テンプレの保存に失敗しました', err)
    });
  }

  loadTemplate(t: TaskTemplate, date: string, user: string, titleInput: HTMLInputElement, startInput: HTMLInputElement, endInput: HTMLInputElement, event: Event): void {
    event.stopPropagation();
    titleInput.value = t.title;
    startInput.value = t.startTime;
    endInput.value = t.endTime;
    
    this.setEditingCategory(date, user, t.category);
    this.setEditingRoom(date, user, t.room);
    this.coWorkers[`${date}-${user}`] = [...t.members];

    this.activeTemplateKey = null; 
    this.cdr.detectChanges();
  }

  deleteTemplate(templateName: string, event: Event): void {
    event.stopPropagation();
    if (!this.currentUser) return;
    this.http.delete(`http://localhost:5099/api/templates/${encodeURIComponent(this.currentUser)}/${encodeURIComponent(templateName)}`).subscribe({
      next: () => this.fetchTemplates(),
      error: (err) => this.handleError('テンプレの削除に失敗しました', err)
    });
  }

  // ==========================================
  // タイムシート・エクセル出力
  // ==========================================
  initTsDates(): void {
    this.tsDates = []; const center = new Date(this.selectedDate);
    for (let i = -14; i <= 14; i++) {
      const d = new Date(center); d.setDate(center.getDate() + i); this.tsDates.push(d.toISOString().split('T')[0]); 
    }
  }

  fetchTimesheet(): void {
    if (this.tsDates.length === 0) return;
    this.http.post<any>('http://localhost:5099/api/timesheet', this.tsDates).subscribe(data => {
      this.timesheet = data; this.cdr.detectChanges();
    });
  }
  getReportData() { return this.viewMode === 'company' ? this.timesheet?.companyReport : this.timesheet?.report; }
  getDailyTotal(date: string): number {
    const report = this.getReportData(); if (!report) return 0;
    return this.categories.reduce((sum, cat) => sum + (report[cat.name]?.[date] || 0), 0);
  }
  get tsCurrentMonthYear(): string {
    if (this.tsDates.length > 0) {
      const [y, m] = this.tsDates[Math.floor(this.tsDates.length / 2)].split('-'); return `${y}年${parseInt(m, 10)}月`;
    } return '';
  }

  onTsWheel(event: WheelEvent): void {
    const container = event.currentTarget as HTMLElement; 
    container.scrollLeft += Math.sign(Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY) * 250; 
    event.preventDefault(); 
  }
  onTsScroll(event: any): void {
    if (this.tsScrollTimer) return; 
    this.tsScrollTimer = setTimeout(() => {
      const el = event.target;
      if (el.scrollLeft >= el.scrollWidth - el.clientWidth - 20) {
        const last = new Date(this.tsDates[this.tsDates.length - 1]); last.setDate(last.getDate() + 1); this.tsDates.push(last.toISOString().split('T')[0]); this.fetchTimesheet();
      } else if (el.scrollLeft <= 20) {
        const oldWidth = el.scrollWidth; const first = new Date(this.tsDates[0]); first.setDate(first.getDate() - 1); this.tsDates.unshift(first.toISOString().split('T')[0]);
        this.cdr.detectChanges(); el.scrollLeft += (el.scrollWidth - oldWidth); this.fetchTimesheet();
      }
      this.tsScrollTimer = null; 
    }, 150); 
  }

  initExportDates(): void {
    const today = new Date(); this.exportStartDate = this.formatDate(new Date(today.getFullYear(), today.getMonth(), 1)); this.exportEndDate = this.formatDate(new Date(today.getFullYear(), today.getMonth() + 1, 0));
  }
  onExportStartDateChange(event: Event) { this.exportStartDate = (event.target as HTMLInputElement).value; }
  onExportEndDateChange(event: Event) { this.exportEndDate = (event.target as HTMLInputElement).value; }

  // 💡 変更：バックエンドでCSVを生成させ、直接ダウンロードURLを開く
  exportToCSVWithRange(): void {
    if (!this.exportStartDate || !this.exportEndDate) return;
    const u = this.viewMode === 'personal' ? encodeURIComponent(this.currentUser) : '';
    const url = `http://localhost:5099/api/export/timesheet?start=${this.exportStartDate}&end=${this.exportEndDate}&viewMode=${this.viewMode}&userName=${u}`;
    window.open(url, '_blank');
  }

  // ==========================================
  // メインボード（データフェッチと更新）
  // ==========================================
  
  // 💡 変更：バックエンドのAPI（ソート・マトリックス生成）を直接叩く
  refreshView(centerDateStr: string): void {
    this.dateRange = [];
    const center = new Date(centerDateStr);
    const start = new Date(center); start.setDate(center.getDate() - 15);
    const end = new Date(center); end.setDate(center.getDate() + 15);
    
    const startDateStr = start.toISOString().split('T')[0];
    const endDateStr = end.toISOString().split('T')[0];

    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
      this.dateRange.push(d.toISOString().split('T')[0]);
    }

    if (this.viewMode === 'personal') {
      const u = encodeURIComponent(this.currentUser);
      this.http.get<{ [d: string]: TodoItem[] }>(`http://localhost:5099/api/todos/range?start=${startDateStr}&end=${endDateStr}&userName=${u}`)
        .subscribe({
          next: (data) => {
            this.personalData = { ...this.personalData, ...data };
            this.cdr.detectChanges();
          },
          error: (err) => this.handleError('タスクの読み込みに失敗しました', err)
        });
    } else {
      const req = { startDate: startDateStr, endDate: endDateStr, teamMembers: this.teamMembers, priorityCategory: '' };
      this.http.post<{ [u: string]: { [d: string]: TodoItem[] } }>(`http://localhost:5099/api/todos/matrix`, req)
        .subscribe({
          next: (data) => {
            Object.keys(data).forEach(u => {
              if (!this.teamMembers.includes(u)) this.addNewUser(u);
              if (!this.matrixData[u]) this.matrixData[u] = {};
              this.matrixData[u] = { ...this.matrixData[u], ...data[u] };
            });
            this.cdr.detectChanges();
          },
          error: (err) => this.handleError('タスクの読み込みに失敗しました', err)
        });
    }

    const userParam = this.viewMode === 'personal' ? encodeURIComponent(this.currentUser) : '';
    this.http.get<{ [date: string]: TodoStats }>(`http://localhost:5099/api/stats/range?start=${startDateStr}&end=${endDateStr}&userName=${userParam}`)
      .subscribe({
        next: (data) => {
          this.dailyStats = { ...this.dailyStats, ...data };
          if (data[this.selectedDate]) this.stats = data[this.selectedDate];
          this.cdr.detectChanges();
        },
        error: (err) => this.handleError('統計の読み込みに失敗しました', err)
      });
  }

  // 💡 変更：1日分のデータ更新もバックエンドに依存
  fetchDailyData(date: string): void {
    const filter = this.filterCategories[date] || '';
    
    if (this.viewMode === 'personal') {
      const u = encodeURIComponent(this.currentUser);
      this.http.get<{ [d: string]: TodoItem[] }>(`http://localhost:5099/api/todos/range?start=${date}&end=${date}&userName=${u}&priorityCategory=${encodeURIComponent(filter)}`)
        .subscribe(res => {
          this.personalData[date] = res[date] || [];
          this.cdr.detectChanges();
        });
    } else {
      const req = { startDate: date, endDate: date, teamMembers: this.teamMembers, priorityCategory: filter };
      this.http.post<{ [u: string]: { [d: string]: TodoItem[] } }>(`http://localhost:5099/api/todos/matrix`, req)
        .subscribe(res => {
          this.teamMembers.forEach(user => {
            if (!this.matrixData[user]) this.matrixData[user] = {};
            this.matrixData[user][date] = res[user]?.[date] || [];
          });
          Object.keys(res).forEach(u => {
            if (!this.teamMembers.includes(u)) {
              this.addNewUser(u);
              if (!this.matrixData[u]) this.matrixData[u] = {};
              this.matrixData[u][date] = res[u]?.[date] || [];
            }
          });
          this.cdr.detectChanges();
        });
    }

    const userParam = this.viewMode === 'personal' ? encodeURIComponent(this.currentUser) : '';
    this.http.get<{ [date: string]: TodoStats }>(`http://localhost:5099/api/stats/range?start=${date}&end=${date}&userName=${userParam}`)
      .subscribe(res => {
        const statsObj = res[date];
        if (statsObj) {
          this.dailyStats[date] = statsObj;
          if (date === this.selectedDate) this.stats = statsObj;
        }
        this.cdr.detectChanges();
      });
  }

  private handleError(userMessage: string, error: any): void {
    console.error('[API Error]', error);
    this.errorMessage = userMessage;
    this.cdr.detectChanges();
    setTimeout(() => {
      this.errorMessage = null;
      this.cdr.detectChanges();
    }, 4000);
  }

  // 💡 変更：並び替え処理（updateMatrixForDate）を削除し、再取得で対応
  onFilterChange(date: string, event: Event): void {
    this.filterCategories[date] = (event.target as HTMLSelectElement).value;
    this.fetchDailyData(date); 
  }

  toggleCoWorker(date: string, baseUser: string, targetUser: string): void {
    const key = `${date}-${baseUser}`; if (!this.coWorkers[key]) this.coWorkers[key] = [];
    const idx = this.coWorkers[key].indexOf(targetUser); if (idx === -1) this.coWorkers[key].push(targetUser); else this.coWorkers[key].splice(idx, 1); this.cdr.detectChanges();
  }
  isCoWorker(date: string, baseUser: string, targetUser: string): boolean { return this.coWorkers[`${date}-${baseUser}`]?.includes(targetUser) || false; }
  clearCoWorkers(date: string, baseUser: string): void { this.coWorkers[`${date}-${baseUser}`] = []; }
  hasCoWorkers(date: string, baseUser: string): boolean { return (this.coWorkers[`${date}-${baseUser}`]?.length || 0) > 0; }

  // ==========================================
  // タスク操作
  // ==========================================
  onInputFocusOut(event: FocusEvent, titleInput: HTMLInputElement, startInput: HTMLInputElement, endInput: HTMLInputElement, date: string, user: string): void {
    if (this.isPopupHovered || this.isTemplateHovered) return; 

    const wrapper = event.currentTarget as HTMLElement;
    if (!wrapper.contains(event.relatedTarget as Node)) {
      this.addTodo(titleInput.value, date, startInput.value, endInput.value, user, titleInput, startInput, endInput);
    }
  }

  addTodo(title: string, date: string, startTime: string, endTime: string, targetUser: string, inputEl: HTMLInputElement, startEl: HTMLInputElement, endEl: HTMLInputElement): void {
    const trimmed = title.trim(); if (!trimmed || this.isAdding) return; 
    this.isAdding = true; 
    
    const category = this.getEditingCategory(date, targetUser);
    const room = this.getEditingRoom(date, targetUser); 
    const allTargets = Array.from(new Set([targetUser, ...(this.coWorkers[`${date}-${targetUser}`] || [])])); 
    
    inputEl.value = ''; startEl.value = ''; endEl.value = '';

    let idx = 0;
    const postNext = () => {
      if (idx >= allTargets.length) {
        this.fetchDailyData(date); this.fetchGlobalInsights(); this.fetchTimesheet(); this.clearCoWorkers(date, targetUser); 
        this.isAdding = false; this.cdr.detectChanges();
        return;
      }
      this.http.post(`${this.apiUrl}/${date}`, { title: trimmed, category, startTime, endTime, room, actualTime: 0, userName: allTargets[idx] }).subscribe({ 
        next: () => { idx++; postNext(); }, 
        error: (err) => { this.handleError('タスクの追加に失敗しました', err); idx++; postNext(); } 
      });
    };
    postNext();
  }

  adjustActualTime(item: TodoItem, date: string, amount: number, event?: Event): void {
    if (event) event.stopPropagation(); 
    const newAct = Math.max(0, item.actualTime + amount);
    this.http.put(`${this.apiUrl}/${date}/${item.id!}/time`, { startTime: item.startTime, endTime: item.endTime, actualTime: newAct }).subscribe(() => {
      this.fetchDailyData(date); this.fetchGlobalInsights(); this.fetchTimesheet(); 
    });
  }

  toggleTodo(id: number, date: string): void {
    this.http.put(`${this.apiUrl}/${date}/toggle/${id}`, {}).subscribe(() => {
      this.fetchDailyData(date); this.fetchGlobalInsights();
    });
  }

  deleteTodo(id: number, date: string): void {
    this.http.delete(`${this.apiUrl}/${date}/${id}`).subscribe(() => {
      this.fetchDailyData(date); this.fetchGlobalInsights(); this.fetchTimesheet();
    });
  }

  copyToTomorrow(id: number, date: string): void {
    this.http.post(`${this.apiUrl}/${date}/${id}/carryover`, {}).subscribe(() => {
      const tmr = new Date(date); tmr.setDate(tmr.getDate() + 1);
      this.fetchDailyData(tmr.toISOString().split('T')[0]); 
    });
  }

  startEditing(item: TodoItem, date: string): void {
    const tasks = this.viewMode === 'personal' ? this.personalData[date] : this.matrixData[item.userName || '']?.[date];
    const relatedTasks = tasks?.filter(t => t.title === item.title && t.category === item.category) || [];
    const members = relatedTasks.map(t => t.userName || '未設定');

    this.editingTaskId = item.id!;
    this.editingTaskData = {
      title: item.title,
      category: item.category,
      startTime: item.startTime || '', 
      endTime: item.endTime || '',    
      room: item.room || '未設定',          
      actTime: item.actualTime,
      members: members
    };
    this.cdr.detectChanges();
  }

  cancelEditing(): void {
    this.editingTaskId = null;
    this.editingTaskData = { title: '', category: '', startTime: '', endTime: '', room: '', actTime: 0, members: [] };
    this.cdr.detectChanges();
  }

  toggleEditMember(member: string): void {
    if (!this.editingTaskData) return;
    const idx = this.editingTaskData.members.indexOf(member);
    if (idx === -1) this.editingTaskData.members.push(member);
    else this.editingTaskData.members.splice(idx, 1);
    this.cdr.detectChanges();
  }

  saveEditing(date: string): void {
    if (!this.editingTaskId || !this.editingTaskData || !this.editingTaskData.title.trim()) return;
    
    this.http.put(`${this.apiUrl}/${date}/${this.editingTaskId}`, this.editingTaskData).subscribe(() => {
      this.editingTaskId = null;
      this.editingTaskData = { title: '', category: '', startTime: '', endTime: '', room: '', actTime: 0, members: [] };
      this.fetchDailyData(date);
      this.fetchTimesheet();
      this.fetchGlobalInsights();
    });
  }

  setHoveredCard(date: string, userName: string): void { this.hoveredCardId = `${date}-${userName}`; }
  clearHoveredCard(): void { this.hoveredCardId = null; }
  isCardHovered(date: string, userName: string): boolean { return this.hoveredCardId === `${date}-${userName}`; }

  trackByDate(i: number, date: string): string { return date; }
  trackByMemberRow(i: number, member: string): number { return i; }
  trackByTodo(i: number, item: TodoItem): string | number { return item.id || item.title; }
  trackByCat(i: number, cat: CategoryInfo): string { return cat.name; }

  formatHours(minutes: number): number | string { return minutes ? parseFloat((minutes / 60).toFixed(2)) : 0; }
  
  // 💡 変更：バックエンド移行に伴う単純化
  getRemainingCountByUser(date: string, userName: string): number { 
    const tasks = this.viewMode === 'personal' ? this.personalData[date] : this.matrixData[userName]?.[date];
    return tasks?.filter(t => !t.isCompleted).length || 0; 
  }

  getChartPercentage(catName: string): number {
    if (!this.stats || !this.stats.timeBreakdown) return 0;
    const totalTime = Object.values(this.stats.timeBreakdown).reduce((a, b) => (a as number) + (b as number), 0) as number; 
    return totalTime === 0 ? 0 : ((this.stats.timeBreakdown[catName] || 0) / totalTime) * 100; 
  }

  // 💡 変更：バックエンドのフォーマット済み文字列をそのまま返す
  getTotalTimeFormatted(): string {
    return this.stats?.totalTimeFormatted || '0分';
  }
  
  getCategoryColor(catName: string): string { return this.categories.find(c => c.name === catName)?.color || '#ccc'; }
  getJapaneseDay(dateStr: string): string { return ['日曜日', '月曜日', '火曜日', '水曜日', '木曜日', '金曜日', '土曜日'][new Date(dateStr).getDay()]; }
  getJapaneseDate(dateStr: string): string { const [, m, d] = dateStr.split('-'); return `${Number(m)}月${Number(d)}日`; }
  
  getCategoryTasksText(catName: string): string {
    let tasks: TodoItem[] = [];
    if (this.viewMode === 'personal') {
      tasks = this.personalData[this.selectedDate]?.filter(t => t.category === catName && t.actualTime > 0) || [];
    } else {
      Object.keys(this.matrixData).forEach(u => {
        const uTasks = this.matrixData[u]?.[this.selectedDate]?.filter(t => t.category === catName && t.actualTime > 0) || [];
        tasks.push(...uTasks);
      });
    }
    if (!tasks || tasks.length === 0) return `${catName}: 記録なし`;
    const details = tasks.map(t => `・${t.title} (${t.actualTime}分) ${t.userName ? '['+t.userName+']' : ''}`).join('\n');
    return `【${catName}】計${tasks.reduce((sum, t) => sum + t.actualTime, 0)}分 (${Math.round(this.getChartPercentage(catName))}%)\n----------------\n${details}`;
  }

  onScroll(event: any): void {
    if (this.scrollTimeout) return; 
    this.scrollTimeout = setTimeout(() => {
      const stage = event.target; const currentLeft = stage.scrollLeft;
      if (Math.abs(currentLeft - this.lastScrollLeft) > 10) {
        if (currentLeft >= stage.scrollWidth - stage.clientWidth - 150) this.appendDays(1); 
        else if (currentLeft <= 150 && currentLeft > 0) {
          const oldWidth = stage.scrollWidth; this.prependDays(1); this.cdr.detectChanges(); 
          stage.style.scrollBehavior = 'auto'; stage.scrollLeft += (stage.scrollWidth - oldWidth); stage.style.scrollBehavior = 'smooth';
        }
        this.lastScrollLeft = currentLeft;
      }
      this.cdr.detectChanges(); this.scrollTimeout = null; 
    }, 100); 
  }
  appendDays(count: number): void {
    const last = new Date(this.dateRange[this.dateRange.length - 1]);
    for(let i = 1; i <= count; i++) { last.setDate(last.getDate() + 1); const dStr = last.toISOString().split('T')[0]; if(!this.dateRange.includes(dStr)) { this.dateRange.push(dStr); this.fetchDailyData(dStr); } }
    if (this.dateRange.length > 40) this.dateRange.shift(); 
  }
  prependDays(count: number): void {
    const first = new Date(this.dateRange[0]);
    for(let i = 1; i <= count; i++) { first.setDate(first.getDate() - 1); const dStr = first.toISOString().split('T')[0]; if(!this.dateRange.includes(dStr)) { this.dateRange.unshift(dStr); this.fetchDailyData(dStr); } }
    if (this.dateRange.length > 40) this.dateRange.pop(); 
  }
}