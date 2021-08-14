using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

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

        [Display(Name = "Country Code")]
        public string CountryCode { get; set; }

        [Display(Name = "Profile Image")]
        public IFormFile ProfileImage { get; set; }
    }
}