using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Okta.AspNetCore;
using Okta.Sdk;
using Okta.Sdk.Configuration;
using OktaProfilePicture.Models;

namespace OktaProfilePicture.Controllers
{
    public class AccountController : Controller
    {
        private readonly OktaClient oktaClient;

        private readonly BlobContainerClient blobContainerClient;

        public AccountController(IConfiguration configuration)
        {
            oktaClient = new OktaClient(new OktaClientConfiguration
            {
                OktaDomain = configuration["Okta:Domain"],
                Token = configuration["Okta:ApiToken"]
            });
            
            var blobServiceClient = new BlobServiceClient(configuration["BlobStorageConnectionString"]);
            blobContainerClient = blobServiceClient.GetBlobContainerClient("okta-profile-picture-container");

            blobContainerClient.CreateIfNotExists();
        }

        public IActionResult SignIn()
        {
            if (!HttpContext.User.Identity.IsAuthenticated)
            {
                return Challenge(OktaDefaults.MvcAuthenticationScheme);
            }

            return RedirectToAction("Index", "Home");
        }

        public async Task<IActionResult> Profile()
        {
            var user = await GetOktaUser();

            var sasBuilder = new BlobSasBuilder
            {
                StartsOn = DateTimeOffset.UtcNow,
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15),
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var url = blobContainerClient.GetBlobClient(user.Profile.GetProperty<string>("profileImageKey")).GenerateSasUri(sasBuilder);

            ViewData["ProfileImageUrl"] = url;

            return View(user);
        }

        public async Task<IActionResult> EditProfile()
        {
            var user = await GetOktaUser();

            return View(new UserProfileViewModel
            {
                City = user.Profile.City,
                Email = user.Profile.Email,
                CountryCode = user.Profile.CountryCode,
                FirstName = user.Profile.FirstName,
                LastName = user.Profile.LastName
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(UserProfileViewModel profile)
        {
            if (ModelState.IsValid)
            {
                var user = await GetOktaUser();
                user.Profile.FirstName = profile.FirstName;
                user.Profile.LastName = profile.LastName;
                user.Profile.Email = profile.Email;
                user.Profile.City = profile.City;
                user.Profile.CountryCode = profile.CountryCode;

                await oktaClient.Users.UpdateUserAsync(user, user.Id, null);

                using (var stream = profile.ProfileImage.OpenReadStream())
                {
                    var blobName = Guid.NewGuid().ToString();
                    await blobContainerClient.UploadBlobAsync(blobName, stream);
                    user.Profile.SetProperty("profileImageKey", blobName);
                }

                return RedirectToAction("Profile");
            }

            return View(profile);
        }

        private async Task<IUser> GetOktaUser()
        {
            var subject = HttpContext.User.Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value;

            return await oktaClient.Users.GetUserAsync(subject);
        }
    }
}