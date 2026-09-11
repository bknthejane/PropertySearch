namespace PropertySearch.Web.Models
{
    public class Property
    {
        public int Id { get; set; }

        public string Title { get; set; } = null!;
        public string? Description { get; set; }

        public PropertyType PropertyType { get; set; }
        public ListingType ListingType { get; set; }

        public decimal Price { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public int Garages { get; set; }
        public int FloorAreaSqm { get; set; }
        public int? ErfSizeSqm { get; set; }

        public DateOnly ListedOn { get; set; }
        public bool IsActive { get; set; }

        public int SuburbId { get; set; }
        public Suburb Suburb { get; set; } = null!;
    }
}
