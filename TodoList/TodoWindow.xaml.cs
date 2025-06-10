using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Google.Cloud.Firestore;

namespace TodoList
{
    public partial class TodoWindow : Window
    {
        private FirestoreDb db;
        private string currentUserId;
        private readonly SqliteDataService _localDb;
        private List<TodoItem> todoItems = new List<TodoItem>();
        private TodoItem currentEditItem = null;
        private List<Tag> tags = new List<Tag>();
        private bool _isOnline;

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
            _localDb = new SqliteDataService();
            _isOnline = CheckInternetConnection();
            LoadData();
        }

        private async void LoadData()
        {
            _isOnline = CheckInternetConnection();

            if (_isOnline)
            {
                try
                {
                    await LoadFromFirebase();
                    await SaveFirebaseDataToLocal(todoItems, tags);
                    await Task.Run(() => SyncLocalWithFirebase());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки из Firebase: {ex.Message}");
                    _isOnline = false;
                    LoadFromLocalDb();
                }
            }
            else
            {
                LoadFromLocalDb();
            }
        }

        private async Task LoadFromFirebase()
        {
            var tasksSnapshot = await GetUserTasksCollection().GetSnapshotAsync();
            todoItems = tasksSnapshot.Documents.Select(ConvertFirebaseToLocalTask).ToList();

            var tagsSnapshot = await GetUserTagsCollection().GetSnapshotAsync();
            tags = tagsSnapshot.Documents.Select(ConvertFirebaseToLocalTag).ToList();

            UpdateUI();
        }

        private void LoadFromLocalDb()
        {
            todoItems = _localDb.GetTasks();
            tags = _localDb.GetTags();
            UpdateUI();
        }

        private async Task SaveFirebaseDataToLocal(List<TodoItem> firebaseTasks, List<Tag> firebaseTags)
        {
            try
            {
                _localDb.DeleteAllTasks();
                _localDb.DeleteAllTags();

                foreach (var task in firebaseTasks)
                {
                    task.FirebaseId = task.Id;
                    _localDb.AddTask(task);
                }

                foreach (var tag in firebaseTags)
                {
                    _localDb.AddTag(tag);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения данных в локальную базу: {ex.Message}");
            }
        }

        private async void SyncLocalWithFirebase()
        {
            var pendingTasks = _localDb.GetPendingSyncTasks();
            foreach (var task in pendingTasks)
            {
                try
                {
                    if (string.IsNullOrEmpty(task.FirebaseId))
                    {
                        var docRef = await GetUserTasksCollection().AddAsync(ConvertLocalToFirebaseTask(task));
                        task.FirebaseId = docRef.Id;
                        task.Id = docRef.Id; // Обновляем локальный ID на Firebase ID
                        _localDb.UpdateTask(task);
                    }
                    else
                    {
                        await GetUserTasksCollection().Document(task.FirebaseId)
                            .SetAsync(ConvertLocalToFirebaseTask(task), SetOptions.MergeAll);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка синхронизации задачи {task.Id}: {ex.Message}");
                }
            }
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
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                    TagIds = new List<string>()
                };

                if (_isOnline)
                {
                    var firebaseTask = ConvertLocalToFirebaseTask(newTask);
                    var docRef = await GetUserTasksCollection().AddAsync(firebaseTask);
                    newTask.Id = docRef.Id;
                    newTask.FirebaseId = docRef.Id;
                    _localDb.AddTask(newTask);
                }
                else
                {
                    newTask.Id = Guid.NewGuid().ToString();
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
                    await GetUserTasksCollection().Document(currentEditItem.FirebaseId)
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

        private async void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            var todoItem = (TodoItem)checkBox.DataContext;

            try
            {
                DocumentReference docRef = GetUserTasksCollection().Document(todoItem.FirebaseId);
                var snapshot = await docRef.GetSnapshotAsync();
                if (!snapshot.Exists)
                {
                    MessageBox.Show($"Документ с ID {todoItem.FirebaseId} не найден в Firestore.");
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
                DocumentReference docRef = GetUserTasksCollection().Document(todoItem.FirebaseId);
                var snapshot = await docRef.GetSnapshotAsync();
                if (!snapshot.Exists)
                {
                    MessageBox.Show($"Документ с ID {todoItem.FirebaseId} не найден в Firestore.");
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
                DocumentReference docRef = GetUserTasksCollection().Document(todoItem.FirebaseId);
                await docRef.DeleteAsync();

                if (_isOnline)
                {
                    _localDb.DeleteTask(todoItem.FirebaseId);
                }
                todoItems.Remove(todoItem);
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления задачи: {ex.Message}");
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
                    { "TaskId", selectedTask.FirebaseId },
                    { "TagId", selectedTag.Id },
                    { "UserId", currentUserId }
                };

                await GetUserTaskTagCollection().AddAsync(taskTag);

                DocumentReference taskDocRef = GetUserTasksCollection().Document(selectedTask.FirebaseId);
                DocumentSnapshot snapshot = await taskDocRef.GetSnapshotAsync();
                if (!snapshot.Exists)
                {
                    MessageBox.Show($"Задача с ID {selectedTask.FirebaseId} не найдена в Firestore.");
                    return;
                }

                if (!selectedTask.TagIds.Contains(selectedTag.Id))
                {
                    selectedTask.TagIds.Add(selectedTag.Id);
                    await taskDocRef.UpdateAsync("TagIds", selectedTask.TagIds);
                }

                MessageBox.Show("Тег успешно добавлен к задаче");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка назначения тега: {ex.Message}");
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
                CreatedAt = doc.ContainsField("CreatedAt") ? doc.GetValue<Timestamp>("CreatedAt") : Timestamp.FromDateTime(DateTime.UtcNow),
                TagIds = doc.GetValue<List<string>>("TagIds") ?? new List<string>()
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

        private Tag ConvertFirebaseToLocalTag(DocumentSnapshot doc)
        {
            return new Tag
            {
                Id = doc.Id,
                Name = doc.GetValue<string>("Name")
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
                 LoadTags();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления тега: {ex.Message}");
            }
        }

        private async void LoadTags()
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
                Query query = GetUserTaskTagCollection()
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

                var tasksQuery = GetUserTasksCollection()
                    .WhereIn(FieldPath.DocumentId, taskIds);

                QuerySnapshot tasksSnapshot = await tasksQuery.GetSnapshotAsync();

                todoItems.Clear();
                foreach (DocumentSnapshot document in tasksSnapshot.Documents)
                {
                    var todo = ConvertFirebaseToLocalTask(document);
                    todoItems.Add(todo);
                }

                UpdateUI();
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

            taskTitleTextBox.Text = currentEditItem.Title;
            taskDescriptionTextBox.Text = currentEditItem.Description;
            actionButton.Content = "Сохранить изменения";
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = searchTextBox.Text?.ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                if (_isOnline)
                {
                    await LoadFromFirebase();
                }
                else
                {
                    LoadFromLocalDb();
                }
                return;
            }

            try
            {
                var filteredItems = todoItems.Where(t => t.Title != null && t.Title.ToLower().StartsWith(searchTerm)).ToList();
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
            }
        }
    }
}