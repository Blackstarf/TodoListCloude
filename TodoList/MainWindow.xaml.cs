using System.Windows;
using Google.Cloud.Firestore;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using System.IO;
using System.Text.Json;

namespace TodoList
{
    public partial class MainWindow : Window
    {
        public static FirestoreDb db;
        private const string AuthFile = "auth.json";
        public static bool isFirebaseInitialized = false;
        public MainWindow()
        {
            InitializeFirebase();
            if (CheckAuth())
            {
                return;
            }
            InitializeComponent();
        }
        private void InitializeFirebase()
        {
            if (isFirebaseInitialized) return;

            try
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS",
                    "D:\\Some projects\\TodoListCloude\\TodoList\\todo-a881c-firebase-adminsdk-fbsvc-a5204aabdd.json");

                if (FirebaseApp.DefaultInstance == null)
                {
                    FirebaseApp.Create(new AppOptions()
                    {
                        Credential = GoogleCredential.GetApplicationDefault(),
                    });
                }

                db = FirestoreDb.Create("todo-a881c");
                isFirebaseInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing Firebase: {ex.Message}");
            }
        }
        private bool CheckAuth()
        {
            if (File.Exists(AuthFile))
            {
                try
                {
                    var userId = LoadUserId();
                    if (!string.IsNullOrEmpty(userId))
                    {
                        // Если Firebase еще не инициализирован, инициализируем его
                        if (!isFirebaseInitialized)
                        {
                            InitializeFirebase();
                        }

                        var todoWindow = new TodoWindow(userId);
                        todoWindow.Show();
                        this.Close();
                        return true;
                    }
                }
                catch
                {
                    // Если файл поврежден, удаляем его
                    File.Delete(AuthFile);
                }
            }
            return false;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string userId = await LoginUser(emailTextBox.Text, passwordBox.Password);
                SaveUserId(userId);

                var todoWindow = new TodoWindow(userId);
                todoWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Login failed: {ex.Message}");
            }
        }

        private async Task<string> LoginUser(string email, string password)
        {
            var userRecord = await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(email);
            return userRecord.Uid;
        }

        private void SaveUserId(string userId)
        {
            File.WriteAllText(AuthFile, JsonSerializer.Serialize(new { UserId = userId }));
        }

        private string LoadUserId()
        {
            var json = File.ReadAllText(AuthFile);
            return JsonSerializer.Deserialize<AuthData>(json).UserId;
        }

        // Метод для выхода из системы (можно вызвать из TodoWindow)
        public static void Logout()
        {
            if (File.Exists(AuthFile))
                File.Delete(AuthFile);
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await RegisterUser(emailTextBox.Text, passwordBox.Password, displayNameTextBox.Text);
                MessageBox.Show("User registered successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Registration failed: {ex.Message}");
            }
        }

        private async Task RegisterUser(string email, string password, string displayName)
        {
            // Создаем пользователя в Authentication
            var userArgs = new UserRecordArgs()
            {
                Email = email,
                Password = password,
                DisplayName = displayName
            };

            var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userArgs);

            // Создаем документ пользователя с подколлекциями
            var userDoc = db.Collection("users").Document(userRecord.Uid);
            await userDoc.SetAsync(new
            {
                Email = email,
                DisplayName = displayName,
                CreatedAt = Timestamp.GetCurrentTimestamp()
            });

            // Инициализируем подколлекции начальными значениями
            await InitializeUserCollections(userDoc);
        }
        private async Task InitializeUserCollections(DocumentReference userDoc)
        {
            // Создаем пустые коллекции для тегов, задач и связей
            var batch = db.StartBatch();

            // Tags
            var tagRef = userDoc.Collection("Tags").Document("placeholder");
            batch.Set(tagRef, new { IsPlaceholder = true });

            // Tasks
            var taskRef = userDoc.Collection("Tasks").Document("placeholder");
            batch.Set(taskRef, new { IsPlaceholder = true });

            // TasksTag
            var taskTagRef = userDoc.Collection("TasksTag").Document("placeholder");
            batch.Set(taskTagRef, new { IsPlaceholder = true });

            await batch.CommitAsync();

            // Удаляем placeholder-документы
            await Task.WhenAll(
                tagRef.DeleteAsync(),
                taskRef.DeleteAsync(),
                taskTagRef.DeleteAsync()
            );
        }
    }
    public class AuthData
    {
        public string UserId { get; set; }
    }
}