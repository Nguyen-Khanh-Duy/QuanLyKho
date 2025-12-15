using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("LearnedPatterns")]
    public class LearnedPattern
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Keyword { get; set; } = string.Empty;

        public string Pattern { get; set; } = string.Empty;

        public int LearnCount { get; set; } = 1;

        public DateTime LastUsed { get; set; } = DateTime.Now;
    }
}
