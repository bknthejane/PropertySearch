namespace PropertySearch.Web.Models
{
    public class Suburb
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? PostalCode { get; set; }

        public int ProvinceId { get; set; }
        public Province Province { get; set; } = null!;

        public ICollection<Property> Properties { get; set; } = new List<Property>();
    }
}
