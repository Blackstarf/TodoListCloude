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
            LoadData();
        }

        private async void LoadTodos()
        {
            try
            {
                // Используем подколлекцию Tasks текущего пользователя
                var tasksCollection = GetUserTasksCollection();
                var snapshot = await tasksCollection.GetSnapshotAsync();

                todoItems.Clear();
                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    var todo = document.ConvertTo<TodoItem>();
                    todo.Id = document.Id;
                    todo.TagIds = document.GetValue<List<string>>("TagIds") ?? new List<string>();
                    todoItems.Add(todo);
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
                // Загрузка данных из Firebase
                var tasksSnapshot = await GetUserTasksCollection().GetSnapshotAsync();
                todoItems = tasksSnapshot.Documents.Select(ConvertFirebaseToLocalTask).ToList();

                var tagsSnapshot = await GetUserTagsCollection().GetSnapshotAsync();
                tags = tagsSnapshot.Documents.Select(ConvertFirebaseToLocalTag).ToList();

                UpdateUI();
            }
            catch
            {
                _isOnline = false;
                LoadFromLocalDb();
            }
        }
        private void LoadFromLocalDb()
        {
            todoItems = _localDb.GetTasks();
            tags = _localDb.GetTags();
            UpdateUI();
        }
        private void SyncLocalWithFirebase()
        {
            // Синхронизация локальных изменений с Firebase
            var pendingTasks = _localDb.GetPendingSyncTasks();
            foreach (var task in pendingTasks)
            {
                if (task.FirebaseId == null)
                {
                    // Создание новой задачи в Firebase
                    var firebaseTask = ConvertLocalToFirebaseTask(task);
                    var docRef = GetUserTasksCollection().AddAsync(firebaseTask).Result;
                    task.FirebaseId = docRef.Id;
                    _localDb.UpdateTask(task);
                }
                else
                {
                    // Обновление существующей задачи
                    GetUserTasksCollection().Document(task.FirebaseId)
                        .SetAsync(ConvertLocalToFirebaseTask(task), SetOptions.Merge);
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
                var tag = new Dictionary<string, object>
        {
            { "Name", tagNameTextBox.Text }
        };

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
                // Получаем все связи задача-тег для выбранного тега
                Query query = db.Collection("taskTags")
                    .WhereEqualTo("TagId", selectedTag.Id)
                    .WhereEqualTo("UserId", currentUserId);

                QuerySnapshot snapshot = await query.GetSnapshotAsync();

                var taskIds = snapshot.Documents.Select(d => d.GetValue<string>("TaskId")).ToList();

                // Получаем задачи по найденным ID
                var tasksQuery = db.Collection("todos")
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

            // Включаем режим редактирования
            editModeCheckBox.IsChecked = true;

            // Заполняем поля данными выбранной задачи
            taskTitleTextBox.Text = currentEditItem.Title;
            taskDescriptionTextBox.Text = currentEditItem.Description;

            // Меняем текст кнопки
            actionButton.Content = "Сохранить изменения";
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            var newTask = new TodoItem
            {
                Title = taskTitleTextBox.Text,
                Description = taskDescriptionTextBox.Text,
                IsComplete = false,
                CreatedAt = DateTime.Now
            };

            if (_isOnline)
            {
                // Работа с Firebase
                var firebaseTask = ConvertLocalToFirebaseTask(newTask);
                var docRef = await GetUserTasksCollection().AddAsync(firebaseTask);
                newTask.FirebaseId = docRef.Id;
            }
            else
            {
                // Сохранение в локальную БД
                _localDb.AddTask(newTask);
            }

            todoItems.Add(newTask);
            UpdateUI();
        }
        private TodoItem ConvertFirebaseToLocalTask(DocumentSnapshot doc)
        {
            return new TodoItem
            {
                FirebaseId = doc.Id,
                Title = doc.GetValue<string>("Title"),
                Description = doc.GetValue<string>("Description"),
                IsComplete = doc.GetValue<bool>("IsDone"),
                CreatedAt = doc.GetValue<Timestamp>("CreatedAt").ToDateTime()
            };
        }
        private Dictionary<string, object> ConvertLocalToFirebaseTask(TodoItem task)
        {
            return new Dictionary<string, object>
        {
            { "Title", task.Title },
            { "Description", task.Description },
            { "IsDone", task.IsComplete },
            { "CreatedAt", Timestamp.FromDateTime(task.CreatedAt) },
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

        private async Task AddTodo(string title, string description)
        {
            var tasksCollection = GetUserTasksCollection();
            var newTask = new Dictionary<string, object>
    {
        { "Title", title },
        { "Description", description ?? string.Empty },
        { "IsComplete", false },
        { "CreatedAt", FieldValue.ServerTimestamp },
        { "TagIds", new List<string>() } // Добавляем инициализацию поля
    };

            await tasksCollection.AddAsync(newTask);
        }

        private async Task UpdateTodo(string id, string title, string description)
        {
            // Используем подколлекцию Tasks
            DocumentReference docRef = GetUserTasksCollection().Document(id);
            await docRef.UpdateAsync(new Dictionary<string, object>
    {
        { "Title", title },
        { "Description", description ?? string.Empty }
    });
        }

        private async void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            var todoItem = (TodoItem)checkBox.DataContext;

            try
            {
                DocumentReference docRef = GetUserTasksCollection().Document(todoItem.Id);
                await docRef.UpdateAsync("IsComplete", true);
                todoItem.IsComplete = true;
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
                await docRef.UpdateAsync("IsComplete", false);
                todoItem.IsComplete = false;
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

    }


    [FirestoreData]
    public class Tag
    {
        [FirestoreProperty]
        public string Name { get; set; }
        public string Id { get; set; }
    }

    [FirestoreData]
    public class TodoItem
    {
        public int Id { get; set; }  // Локальный ID
        public string FirebaseId { get; set; } // ID в Firebase
        [FirestoreProperty]
        public string Title { get; set; }

        [FirestoreProperty]
        public string Description { get; set; }

        [FirestoreProperty]
        public bool IsComplete { get; set; }

        [FirestoreProperty]
        public Timestamp CreatedAt { get; set; }
        public List<string> TagIds { get; set; } = new List<string>();
    }

    [FirestoreData]
    public class TaskTag
    {
        public string FirebaseId { get; set; } // ID в Firebase
        public string TaskId { get; set; }
        public string TagId { get; set; }
        public string UserId { get; set; }
        public string Id { get; set; }
    }
}