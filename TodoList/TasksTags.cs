using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TodoList
{

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
        public string Id { get; set; }  // Локальный ID
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
