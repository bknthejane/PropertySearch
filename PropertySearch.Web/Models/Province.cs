namespace PropertySearch.Web.Models
{
    public class Province
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;

        public ICollection<Suburb> Suburbs { get; set; } = new List<Suburb>();
    }
}
