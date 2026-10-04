namespace QALab.Sandbox
{
    /// <summary>A spawned enemy (data only; the sandbox has no enemy AI).</summary>
    public sealed class Enemy
    {
        public Enemy(string id) { Id = id; }

        /// <summary><c>enemy_&lt;8 hex chars&gt;</c>, unique per spawn.</summary>
        public string Id { get; }

        /// <summary>Id of the enemy this one follows, or null.</summary>
        public string TargetId { get; set; }
    }
}
