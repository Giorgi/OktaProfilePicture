using System.ComponentModel.DataAnnotations;

namespace OktaProfilePicture.Models
{
    public class UserProfileViewModel
    {
        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        public string City { get; set; }

        public string CountryCode { get; set; }
    }
}