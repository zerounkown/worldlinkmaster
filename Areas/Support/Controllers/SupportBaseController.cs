using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WorldLinkMaster.Web.Areas.Support.Controllers;

[Area("Support")]
[Authorize(Roles = "Admin,Support")]
public abstract class SupportBaseController : Controller
{
}
