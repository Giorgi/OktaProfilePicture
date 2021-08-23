using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
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
        private readonly FaceClient faceClient;
        private readonly BlobContainerClient blobContainerClient;

        public AccountController(IConfiguration configuration)
        {
            oktaClient = new OktaClient(new OktaClientConfiguration
            {
                OktaDomain = configuration["Okta:Domain"],
                Token = configuration["Okta:ApiToken"]
            });

            var blobServiceClient = new BlobServiceClient(configuration["Azure:BlobStorageConnectionString"]);
            blobContainerClient = blobServiceClient.GetBlobContainerClient("okta-profile-picture-container");

            blobContainerClient.CreateIfNotExists();

            faceClient = new FaceClient(new ApiKeyServiceClientCredentials(configuration["Azure:SubscriptionKey"])) { Endpoint = configuration["Azure:FaceClientEndpoint"] };
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
            if (!ModelState.IsValid)
            {
                return View(profile);
            }

            var user = await GetOktaUser();
            user.Profile.FirstName = profile.FirstName;
            user.Profile.LastName = profile.LastName;
            user.Profile.Email = profile.Email;
            user.Profile.City = profile.City;
            user.Profile.CountryCode = profile.CountryCode;

            var stream = profile.ProfileImage.OpenReadStream();
            var detectedFaces = await faceClient.Face.DetectWithStreamAsync(stream, recognitionModel: RecognitionModel.Recognition04,
                                                                            detectionModel: DetectionModel.Detection01);

            if (detectedFaces.Count != 1 || detectedFaces[0].FaceId == null)
            {
                ModelState.AddModelError("", $"Detected {detectedFaces.Count} faces instead of 1 face");
                return View(profile);
            }

            var personGroupId = user.Id.ToLower();

            if (string.IsNullOrEmpty(user.Profile.GetProperty<string>("personId")))
            {
                await faceClient.PersonGroup.CreateAsync(personGroupId, user.Profile.Login,
                                                         recognitionModel: RecognitionModel.Recognition04);

                stream = profile.ProfileImage.OpenReadStream();
                var personId = (await faceClient.PersonGroupPerson.CreateAsync(personGroupId, user.Profile.Login)).PersonId;
                await faceClient.PersonGroupPerson.AddFaceFromStreamAsync(personGroupId, personId, stream);

                user.Profile["personId"] = personId;

                await UpdateUserImage();
            }
            else
            {
                var faceId = detectedFaces[0].FaceId.Value;

                var personId = new Guid(user.Profile.GetProperty<string>("personId"));
                var verifyResult = await faceClient.Face.VerifyFaceToPersonAsync(faceId, personId, personGroupId);

                if (verifyResult.IsIdentical && verifyResult.Confidence >= 0.8)
                {
                    await UpdateUserImage();
                }
                else
                {
                    ModelState.AddModelError("", "The uploaded picture doesn't match your current picture");
                    return View(profile);
                }
            }

            await oktaClient.Users.UpdateUserAsync(user, user.Id, null);
            return RedirectToAction("Profile");

            async Task UpdateUserImage()
            {
                var blobName = Guid.NewGuid().ToString();
                await blobContainerClient.DeleteBlobAsync(user.Profile.GetProperty<string>("profileImageKey"));
                await blobContainerClient.UploadBlobAsync(blobName, profile.ProfileImage.OpenReadStream());
                user.Profile.SetProperty("profileImageKey", blobName);
            }
        }

        private async Task<IUser> GetOktaUser()
        {
            var subject = HttpContext.User.Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value;

            return await oktaClient.Users.GetUserAsync(subject);
        }
    }
}