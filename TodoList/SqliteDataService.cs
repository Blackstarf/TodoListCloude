using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.Data.Sqlite;

namespace TodoList
{
    public class SqliteDataService
    {
        private readonly string _connectionString;

        public SqliteDataService()
        {
            _connectionString = $"Data Source={"C:\\Users\\Student\\source\\repos\\TodoList\\TodoList\\bin\\Debug\\TaskBase.db"}";

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
                Title TEXT NOT NULL,
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
                tasks.Add(new TodoItem
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsComplete = reader.GetBoolean(3),
                    Deadline = DateTime.Parse(reader.GetString(4)),
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
            INSERT INTO Tasks (Title, Description, IsDone, Deadline, FirebaseId)
            VALUES ($title, $desc, $done, $deadline, $firebaseId)";

            command.Parameters.AddWithValue("$title", task.Title);
            command.Parameters.AddWithValue("$desc", task.Description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$done", task.IsComplete ? 1 : 0);
            command.Parameters.AddWithValue("$deadline", task.Deadline.ToString("O"));
            command.Parameters.AddWithValue("$firebaseId", task.FirebaseId ?? (object)DBNull.Value);

            command.ExecuteNonQuery();
            task.Id = (int)connection.LastInsertRowId;
        }

        public void UpdateTask(TodoItem task)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
            UPDATE Tasks 
            SET Title = $title, 
                Description = $desc, 
                IsDone = $done, 
                Deadline = $deadline, 
                FirebaseId = $firebaseId 
            WHERE Id = $id";

            command.Parameters.AddWithValue("$id", task.Id);
            command.Parameters.AddWithValue("$title", task.Title);
            command.Parameters.AddWithValue("$desc", task.Description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$done", task.IsComplete ? 1 : 0);
            command.Parameters.AddWithValue("$deadline", task.Deadline.ToString("O"));
            command.Parameters.AddWithValue("$firebaseId", task.FirebaseId ?? (object)DBNull.Value);

            command.ExecuteNonQuery();
        }

        public void DeleteTask(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Удаляем связанные теги
            var deleteTagsCommand = connection.CreateCommand();
            deleteTagsCommand.CommandText = "DELETE FROM TasksTag WHERE TaskId = $id";
            deleteTagsCommand.Parameters.AddWithValue("$id", id);
            deleteTagsCommand.ExecuteNonQuery();

            // Удаляем саму задачу
            var deleteTaskCommand = connection.CreateCommand();
            deleteTaskCommand.CommandText = "DELETE FROM Tasks WHERE Id = $id";
            deleteTaskCommand.Parameters.AddWithValue("$id", id);
            deleteTaskCommand.ExecuteNonQuery();
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
                    Id = reader.GetInt32(0),
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
            command.CommandText = "INSERT INTO Tags (Name) VALUES ($name)";
            command.Parameters.AddWithValue("$name", tag.Name);

            command.ExecuteNonQuery();
            tag.Id = (int)connection.LastInsertRowId;
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
                tasks.Add(new TodoItem
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsComplete = reader.GetBoolean(3),
                    Deadline = DateTime.Parse(reader.GetString(4)),
                    FirebaseId = null
                });
            }
            return tasks;
        }
    }
}
