using Google.Cloud.Firestore;
using Microsoft.Data.Sqlite;

namespace TodoList
{
    public class SqliteDataService
    {
        public static string _connectionString;

        public SqliteDataService()
        {
            _connectionString = $"Data Source={"C:\\Users\\B-ZONE\\Desktop\\TodoListCloude\\TodoList\\bin\\Debug\\TaskBase.db"}";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Создание таблиц
            var command = connection.CreateCommand();
            command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Tags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Tasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                IsDone INTEGER DEFAULT 0,
                Deadline TEXT,
                FirebaseId TEXT
            );

            CREATE TABLE IF NOT EXISTS TasksTag (
                TaskId INTEGER NOT NULL,
                TagId INTEGER NOT NULL,
                PRIMARY KEY (TaskId, TagId),
                FOREIGN KEY (TaskId) REFERENCES Tasks(Id),
                FOREIGN KEY (TagId) REFERENCES Tags(Id)
            );";
            command.ExecuteNonQuery();
        }
        public List<TodoItem> GetTasks()
        {
            var tasks = new List<TodoItem>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Tasks";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string createdAtString = reader.GetString(4); // Get the raw string
                string dateTimeString = createdAtString.Replace("Timestamp:", "").Trim();
                DateTime dateTime = DateTime.Parse(dateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind); // Parse with UTC awareness
                tasks.Add(new TodoItem
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsComplete = reader.GetBoolean(3),
                    CreatedAt = Timestamp.FromDateTime(dateTime.ToUniversalTime()), // Ensure UTC
                    FirebaseId = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
            return tasks;
        }

        public void AddTask(TodoItem task)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
    INSERT INTO Tasks (Name, Description, IsDone, Deadline, FirebaseId)
    VALUES ($name, $desc, $done, $deadline, $firebaseId);
    SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("$name", task.Title);
            command.Parameters.AddWithValue("$desc", task.Description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$done", task.IsComplete ? 1 : 0);
            command.Parameters.AddWithValue("$deadline", task.CreatedAt.ToString());
            command.Parameters.AddWithValue("$firebaseId", task.FirebaseId ?? (object)DBNull.Value);

            // Получаем ID последней вставленной записи
            task.Id = command.ExecuteScalar().ToString();
        }

        public void UpdateTask(TodoItem task)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
            UPDATE Tasks 
            SET Name = $name, 
                Description = $desc, 
                IsDone = $done, 
                Deadline = $deadline, 
                FirebaseId = $firebaseId 
            WHERE Id = $id";

            command.Parameters.AddWithValue("$id", task.Id);
            command.Parameters.AddWithValue("$name", task.Title);
            command.Parameters.AddWithValue("$desc", task.Description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$done", task.IsComplete ? 1 : 0);
            command.Parameters.AddWithValue("$deadline", task.CreatedAt.ToString());
            command.Parameters.AddWithValue("$firebaseId", task.FirebaseId ?? (object)DBNull.Value);

            command.ExecuteNonQuery();
        }

        public void DeleteTask(string id)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Tasks WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }
        public void DeleteAllTasks()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Tasks";
            command.ExecuteNonQuery();
        }

        public void DeleteAllTags()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Tags";
            command.ExecuteNonQuery();
        }
        public List<Tag> GetTags()
        {
            var tags = new List<Tag>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Tags";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tags.Add(new Tag
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1)
                });
            }
            return tags;
        }

        public void AddTag(Tag tag)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
    INSERT INTO Tags (Name) VALUES ($name);
    SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$name", tag.Name);

            // Получаем ID последней вставленной записи
            tag.Id = command.ExecuteScalar().ToString();
        }

        public void AssignTag(int taskId, int tagId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
            INSERT INTO TasksTag (TaskId, TagId)
            VALUES ($taskId, $tagId)
            ON CONFLICT DO NOTHING";

            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$tagId", tagId);

            command.ExecuteNonQuery();
        }

        public List<TaskTag> GetTaskTags(int taskId)
        {
            var taskTags = new List<TaskTag>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM TasksTag WHERE TaskId = $taskId";
            command.Parameters.AddWithValue("$taskId", taskId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                taskTags.Add(new TaskTag
                {
                    TaskId = reader.GetString(0),
                    TagId = reader.GetString(1)
                });
            }
            return taskTags;
        }

        public List<TodoItem> GetPendingSyncTasks()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Tasks WHERE FirebaseId IS NULL";

            var tasks = new List<TodoItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string createdAtString = reader.GetString(4); // Get the raw string
                string dateTimeString = createdAtString.Replace("Timestamp:", "").Trim();
                DateTime dateTime = DateTime.Parse(dateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind);
                tasks.Add(new TodoItem
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsComplete = reader.GetBoolean(3),
                    CreatedAt = Timestamp.FromDateTime(dateTime.ToUniversalTime()), // Ensure UTC
                    FirebaseId = null
                });
            }
            return tasks;
        }
        public void UpdateTag(Tag tag)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Tags 
        SET Name = $name
        WHERE Id = $id";

            command.Parameters.AddWithValue("$id", tag.Id);
            command.Parameters.AddWithValue("$name", tag.Name);

            command.ExecuteNonQuery();
        }
    }
}