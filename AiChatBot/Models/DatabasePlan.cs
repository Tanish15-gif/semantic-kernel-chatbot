namespace Ai.Models
{
    public class DataBasePlan
    {
        public string? Operation { get; set; }
        public string? DataBaseName { get; set; }
        public string? TableName { get; set; }
        public string? Query { get; set; }
        public List<ColumnDefinition>? Columns { get; set; }
    }

    public class ColumnDefinition
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsNullable { get; set; } = true;
    }
}
