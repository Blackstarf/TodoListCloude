using System.Windows;
using System.Windows.Controls;
using Google.Cloud.Firestore;

namespace TodoList
{
    public partial class TodoWindow : Window
    {
        private FirestoreDb db;
        private string currentUserId;
        private readonly SqliteDataService _sqliteService;
        private List<TodoItem> todoItems = new List<TodoItem>();
        private TodoItem currentEditItem = null;
        private List<Tag> tags = new List<Tag>();
        private bool _isOnline = CheckInternetConnection();
        private readonly SqliteDataService _localDb;

        // Для работы с тегами
        private CollectionReference GetUserTagsCollection()
            => db.Collection("users").Document(currentUserId).Collection("Tags");

        // Для работы с задачами
        private CollectionReference GetUserTasksCollection()
            => db.Collection("users").Document(currentUserId).Collection("Tasks");

        // Для связей задача-тег
        private CollectionReference GetUserTaskTagCollection()
            => db.Collection("users").Document(currentUserId).Collection("TasksTag");

        public TodoWindow(string userId)
        {
            InitializeComponent();
            this.currentUserId = userId;
            this.db = MainWindow.db;
            _localDb = new SqliteDataService(); // Initialize _localDb
            LoadData();
        }

        private async void LoadTodos()
        {
            try
            {
                var tasksCollection = GetUserTasksCollection();
                var snapshot = await tasksCollection.GetSnapshotAsync();

                todoItems.Clear();
                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    var todo = document.ConvertTo<TodoItem>();
                    todo.Id = document.Id;
                    todo.TagIds = document.GetValue<List<string>>("TagIds") ?? new List<string>();
                    todoItems.Add(todo);
                    Console.WriteLine($"Loaded task: {todo.Title}"); // Debug output
                }

                todoListView.ItemsSource = null;
                todoListView.ItemsSource = todoItems;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки задач: {ex.Message}");
            }
        }

        private async void LoadData()
        {
            if (_isOnline)
            {
                await LoadFromFirebase();
                SyncLocalWithFirebase();
            }
            else
            {
                LoadFromLocalDb();
            }
        }

        private async Task LoadFromFirebase()
        {
            try
            {
                var tasksSnapshot = await GetUserTasksCollection().GetSnapshotAsync();
                todoItems = tasksSnapshot.Documents.Select(ConvertFirebaseToLocalTask).ToList();

                var tagsSnapshot = await GetUserTagsCollection().GetSnapshotAsync();
                tags = tagsSnapshot.Documents.Select(ConvertFirebaseToLocalTag).ToList();

                if (_isOnline)
                {
                    _localDb.DeleteAllTasks();
                    _localDb.DeleteAllTags();
                }

                await SaveFirebaseDataToLocal(todoItems, tags);
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading from Firebase: {ex.Message}\nStackTrace: {ex.StackTrace}");
                _isOnline = false;
                LoadFromLocalDb();
            }
        }

        private Tag ConvertFirebaseToLocalTag(DocumentSnapshot doc)
        {
            return new Tag
            {
                Id = doc.Id,
                Name = doc.GetValue<string>("Name")
            };
        }

        private void LoadFromLocalDb()
        {
            todoItems = _localDb.GetTasks();
            tags = _localDb.GetTags();
            UpdateUI();
            Console.WriteLine($"Loaded {todoItems.Count} tasks from local DB"); // Debug output
        }

        private void SyncLocalWithFirebase()
        {
            var pendingTasks = _localDb.GetPendingSyncTasks();
            foreach (var task in pendingTasks)
            {
                if (task.FirebaseId == null)
                {
                    var firebaseTask = ConvertLocalToFirebaseTask(task);
                    var docRef = GetUserTasksCollection().AddAsync(firebaseTask).Result;
                    task.FirebaseId = docRef.Id;
                    _localDb.UpdateTask(task);
                }
                else
                {
                    GetUserTasksCollection().Document(task.FirebaseId)
                        .SetAsync(ConvertLocalToFirebaseTask(task), SetOptions.MergeAll);
                }
            }
        }

        private async Task LoadTags()
        {
            try
            {
                var tagsCollection = GetUserTagsCollection();
                var snapshot = await tagsCollection.GetSnapshotAsync();

                tags.Clear();
                foreach (var document in snapshot.Documents)
                {
                    var tag = document.ConvertTo<Tag>();
                    tag.Id = document.Id;
                    tags.Add(tag);
                }

                tagsListBox.ItemsSource = null;
                tagsListBox.ItemsSource = tags;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки тегов: {ex.Message}");
            }
        }

        private async void AddTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tagNameTextBox.Text))
            {
                MessageBox.Show("Введите название тега");
                return;
            }

            try
            {
                var tag = new Dictionary<string, object> { { "Name", tagNameTextBox.Text } };
                await GetUserTagsCollection().AddAsync(tag);
                tagNameTextBox.Clear();
                await LoadTags();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления тега: {ex.Message}");
            }
        }

        private async void AssignTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (todoListView.SelectedItem == null || tagsListBox.SelectedItem == null)
            {
                MessageBox.Show("Выберите задачу и тег");
                return;
            }

            var selectedTask = (TodoItem)todoListView.SelectedItem;
            var selectedTag = (Tag)tagsListBox.SelectedItem;

            try
            {
                var taskTag = new Dictionary<string, object>
                {
                    { "TaskId", selectedTask.Id },
                    { "TagId", selectedTag.Id }
                };

                await GetUserTaskTagCollection().AddAsync(taskTag);

                if (!selectedTask.TagIds.Contains(selectedTag.Id))
                {
                    selectedTask.TagIds.Add(selectedTag.Id);
                    await GetUserTasksCollection().Document(selectedTask.Id)
                        .UpdateAsync("TagIds", selectedTask.TagIds);
                }

                MessageBox.Show("Тег успешно добавлен к задаче");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка назначения тега: {ex.Message}");
            }
        }

        private async void ShowTaggedTasksButton_Click(object sender, RoutedEventArgs e)
        {
            if (tagsListBox.SelectedItem == null)
            {
                MessageBox.Show("Выберите тег");
                return;
            }

            var selectedTag = (Tag)tagsListBox.SelectedItem;

            try
            {
                Query query = db.Collection("taskTags")
                    .WhereEqualTo("TagId", selectedTag.Id)
                    .WhereEqualTo("UserId", currentUserId);

                QuerySnapshot snapshot = await query.GetSnapshotAsync();

                var taskIds = snapshot.Documents.Select(d => d.GetValue<string>("TaskId")).ToList();

                if (taskIds.Count == 0)
                {
                    MessageBox.Show("Нет задач с выбранным тегом.");
                    todoItems.Clear();
                    todoListView.ItemsSource = null;
                    todoListView.ItemsSource = todoItems;
                    return;
                }

                var tasksQuery = db.Collection("Tasks")
                    .WhereIn(FieldPath.DocumentId, taskIds);

                QuerySnapshot tasksSnapshot = await tasksQuery.GetSnapshotAsync();

                todoItems.Clear();
                foreach (DocumentSnapshot document in tasksSnapshot.Documents)
                {
                    var todo = document.ConvertTo<TodoItem>();
                    todo.Id = document.Id;
                    todoItems.Add(todo);
                }

                todoListView.ItemsSource = null;
                todoListView.ItemsSource = todoItems;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка фильтрации задач: {ex.Message}");
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            currentEditItem = (TodoItem)button.DataContext;

            editModeCheckBox.IsChecked = true;
            taskTitleTextBox.Text = currentEditItem.Title;
            taskDescriptionTextBox.Text = currentEditItem.Description;
            actionButton.Content = "Сохранить изменения";
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentEditItem == null)
            {
                var newTask = new TodoItem
                {
                    Title = taskTitleTextBox.Text,
                    Description = taskDescriptionTextBox.Text,
                    IsComplete = false,
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };

                if (_isOnline)
                {
                    var firebaseTask = ConvertLocalToFirebaseTask(newTask);
                    var docRef = await GetUserTasksCollection().AddAsync(firebaseTask);
                    newTask.FirebaseId = docRef.Id;
                    newTask.Id = docRef.Id;
                    _localDb.AddTask(newTask);
                }
                else
                {
                    _localDb.AddTask(newTask);
                }

                todoItems.Add(newTask);
            }
            else
            {
                currentEditItem.Title = taskTitleTextBox.Text;
                currentEditItem.Description = taskDescriptionTextBox.Text;

                if (_isOnline)
                {
                    var firebaseTask = ConvertLocalToFirebaseTask(currentEditItem);
                    await GetUserTasksCollection().Document(currentEditItem.Id)
                        .SetAsync(firebaseTask, SetOptions.MergeAll);
                    _localDb.UpdateTask(currentEditItem);
                }
                else
                {
                    _localDb.UpdateTask(currentEditItem);
                }

                currentEditItem = null;
                actionButton.Content = "Добавить задачу";
            }

            UpdateUI();
        }

        private async Task SaveFirebaseDataToLocal(List<TodoItem> firebaseTasks, List<Tag> firebaseTags)
        {
            try
            {
                foreach (var task in firebaseTasks)
                {
                    var existingTask = _localDb.GetTasks().FirstOrDefault(t => t.FirebaseId == task.Id);

                    if (existingTask == null)
                    {
                        task.FirebaseId = task.Id;
                        _localDb.AddTask(task);
                    }
                    else
                    {
                        task.Id = existingTask.Id;
                        task.FirebaseId = task.Id;
                        _localDb.UpdateTask(task);
                    }
                }

                foreach (var tag in firebaseTags)
                {
                    var existingTag = _localDb.GetTags().FirstOrDefault(t => t.Id == tag.Id);

                    if (existingTag == null)
                    {
                        _localDb.AddTag(tag);
                    }
                    else
                    {
                        _localDb.UpdateTag(tag);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении данных в локальную БД: {ex.Message}");
            }
        }

        private TodoItem ConvertFirebaseToLocalTask(DocumentSnapshot doc)
        {
            return new TodoItem
            {
                Id = doc.Id,
                FirebaseId = doc.Id,
                Title = doc.GetValue<string>("Title") ?? "No Title",
                Description = doc.GetValue<string>("Description") ?? "",
                IsComplete = doc.GetValue<bool?>("IsDone") ?? false,
                CreatedAt = doc.ContainsField("CreatedAt") ? doc.GetValue<Timestamp>("CreatedAt") : Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        private Dictionary<string, object> ConvertLocalToFirebaseTask(TodoItem task)
        {
            return new Dictionary<string, object>
            {
                { "Title", task.Title },
                { "Description", task.Description },
                { "IsDone", task.IsComplete },
                { "CreatedAt", task.CreatedAt },
                { "TagIds", task.TagIds }
            };
        }

        private void UpdateUI()
        {
            Dispatcher.Invoke(() =>
            {
                todoListView.ItemsSource = null;
                todoListView.ItemsSource = todoItems;
                tagsListBox.ItemsSource = null;
                tagsListBox.ItemsSource = tags;
            });
        }

        private async void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            var todoItem = (TodoItem)checkBox.DataContext;

            try
            {
                DocumentReference docRef = GetUserTasksCollection().Document(todoItem.Id);
                var snapshot = await docRef.GetSnapshotAsync();
                if (!snapshot.Exists)
                {
                    MessageBox.Show($"Документ с ID {todoItem.Id} не найден в Firestore.");
                    checkBox.IsChecked = false;
                    return;
                }
                await docRef.UpdateAsync("IsComplete", true);
                todoItem.IsComplete = true;

                if (_isOnline)
                {
                    _localDb.UpdateTask(todoItem);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления задачи: {ex.Message}");
                checkBox.IsChecked = false;
            }
        }

        private async void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            var todoItem = (TodoItem)checkBox.DataContext;

            try
            {
                DocumentReference docRef = GetUserTasksCollection().Document(todoItem.Id);
                var snapshot = await docRef.GetSnapshotAsync();
                if (!snapshot.Exists)
                {
                    MessageBox.Show($"Документ с ID {todoItem.Id} не найден в Firestore.");
                    checkBox.IsChecked = true;
                    return;
                }
                await docRef.UpdateAsync("IsComplete", false);
                todoItem.IsComplete = false;

                if (_isOnline)
                {
                    _localDb.UpdateTask(todoItem);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления задачи: {ex.Message}");
                checkBox.IsChecked = true;
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var todoItem = (TodoItem)button.DataContext;

            try
            {
                DocumentReference docRef = GetUserTasksCollection().Document(todoItem.Id);
                await docRef.DeleteAsync();

                if (_isOnline)
                {
                    _localDb.DeleteTask(todoItem.Id);
                }
                LoadTodos();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления задачи: {ex.Message}");
            }
        }

        private static bool CheckInternetConnection()
        {
            try
            {
                using var client = new System.Net.WebClient();
                using var stream = client.OpenRead("http://www.google.com");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = searchTextBox.Text?.ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                if (_isOnline)
                {
                     LoadTodos(); // Reload all tasks from Firebase
                }
                else
                {
                    todoListView.ItemsSource = null;
                    todoListView.ItemsSource = todoItems;
                }
                return;
            }

            Console.WriteLine($"Searching for prefix: {searchTerm}"); // Debug output
            Console.WriteLine($"Total tasks before filter: {todoItems.Count}"); // Debug output
            foreach (var item in todoItems)
            {
                Console.WriteLine($"Task available: {item.Title} (ID: {item.Id})"); // Debug all tasks
            }

            try
            {
                var filteredItems = todoItems.Where(t => t.Title != null && t.Title.ToLower().StartsWith(searchTerm)).ToList();
                Console.WriteLine($"Filtered items count: {filteredItems.Count}"); // Debug output
                foreach (var item in filteredItems)
                {
                    Console.WriteLine($"Matched task: {item.Title} (ID: {item.Id})"); // Debug matched tasks
                }

                todoListView.ItemsSource = null;
                todoListView.ItemsSource = filteredItems;

                if (filteredItems.Count == 0)
                {
                    MessageBox.Show("Нет задач по запросу.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка поиска: {ex.Message}");
                Console.WriteLine($"Search error: {ex.Message}");
            }
        }
    }
}