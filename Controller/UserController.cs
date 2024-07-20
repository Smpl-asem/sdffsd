using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kavenegar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
[Route("[Action]")]
[ApiController]
public class UserController : Controller
{
    private readonly string salt = "S@lt?";
    private readonly Context db;
    public UserController(Context _db)
    {
        db = _db;
    }

    [HttpPost]
    public IActionResult Register(DtodUser user)
    {
        Users check = db.Users_tbl.FirstOrDefault(x => x.Username == user.Username || x.NatinalCode == user.NatinalCode || x.Phone == user.Phone);
        if (check != null)
        {
            if (check.Username == user.Username)
            {
                return Ok("Invalid Username");
            }
            else if (check.NatinalCode == user.NatinalCode)
            {
                return Ok("Invalid Natinal Code");
            }
            else if (check.Phone == user.Phone)
            {
                return Ok("Invalid Phone");
            }
        }
        var NewUser = new Users
        {
            Username = user.Username.ToLower(),
            Password = BCrypt.Net.BCrypt.HashPassword(user.Password + salt + user.Username.ToLower()),
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.Phone,
            Addres = user.Addres,
            NatinalCode = user.NatinalCode,
            PerconalCode = user.PerconalCode,
            Profile = Uploadimage.Upload(user.Profile),
            CreateDateTime = DateTime.Now
        };
        db.Users_tbl.Add(NewUser);
        db.SaveChanges();
        CreateUserLog((int)NewUser.Id, 3, true, 3);
        return Ok("Succesful !");
    }

    [HttpPost]
    public IActionResult Login(string Username, string Password)
    {
        Users check = db.Users_tbl.FirstOrDefault(x => x.Username == Username.ToLower());
        if (check == null)
        {
            return NotFound($"{Username} not found");
        }
        else if (!BCrypt.Net.BCrypt.Verify(Password + salt + Username.ToLower(), check.Password))
        {
            CreateUserLog((int)check.Id, 1, false, 3);
            return Ok("Invalid Password !");
        }
        else
        {
            CreateUserLog((int)check.Id, 1, true, 4);
            return Ok(CreateToken(Username.ToLower()));
        }
    }

    [HttpPut]
    public IActionResult ResetPassword(string Username, string NatinalCode)
    {
        Users check = db.Users_tbl.FirstOrDefault(x => x.Username == Username.ToLower() && x.NatinalCode == NatinalCode);
        if (check == null)
        {
            return Ok("Invalid Data");
        }

        // sms check
        smsUser request = db.sms_tbl.FirstOrDefault(x => x.UserId == check.Id);



        if (request != null)
        {
            if (DateTime.Now.AddMinutes(-10) < request.CreateDateTime)
            {

                CreateUserLog((int)check.Id, 4, false, 3, "spam");

                return Ok("you Must Wait about 10 min");
            }
            else
            {
                db.sms_tbl.Remove(request);
            }
        }
        Random random = new Random();
        smsUser newSms = new smsUser
        {
            TryCount = 0,
            SmsCode = random.Next(100000, 999999).ToString(),
            UserId = (int)check.Id,
            IsValid = true,
            CreateDateTime = DateTime.Now
        };
        db.sms_tbl.Add(newSms);
        db.SaveChanges();

        CreateUserLog((int)check.Id, 4, true, 3);

        return Ok(SmsCode(newSms.SmsCode, check.Phone)
        );
    }

    [HttpPut]
    public IActionResult VerifyPassword(string Username, string NewPassword, string ConfirmPassword, string Code)
    {
        if (NewPassword != ConfirmPassword)
        {
            return Ok("Passwords Are not Match");
        }
        Users check = db.Users_tbl.FirstOrDefault(x => x.Username == Username.ToLower());
        if (check == null)
        {
            return Ok("Invalid User");
        }

        //sms Check
        smsUser smsCheck = db.sms_tbl.FirstOrDefault(x => x.UserId == check.Id);
        if (smsCheck == null)
        {
            CreateUserLog((int)check.Id, 5, false, 5, "Try Without Code");
            return Ok("Haven't Code Requset. try Reset First");

        }
        else if (DateTime.Now.AddMinutes(-10) > smsCheck.CreateDateTime)
        { //Time Passed
            db.sms_tbl.Remove(smsCheck);
            db.SaveChanges();
            CreateUserLog((int)check.Id, 5, false, 5, "Code Expire");
            return Ok("Code Time Expire ... Try again");
        }
        else if (smsCheck.IsValid == true)
        {
            if (Code == smsCheck.SmsCode)
            {
                check.Password = BCrypt.Net.BCrypt.HashPassword(NewPassword + salt + Username.ToLower());
                db.Users_tbl.Update(check);
                db.sms_tbl.Remove(smsCheck);
                db.SaveChanges();
                CreateUserLog((int)check.Id, 5, true, 2);
                return Ok("Sucssesful");
            }
            else
            {
                if (smsCheck.TryCount > 3) // start from 0 -> 1,2,3,4 -> when 4 still can try 5 ! done
                    smsCheck.IsValid = false;
                else
                    ++smsCheck.TryCount;
                db.sms_tbl.Update(smsCheck);
                db.SaveChanges();
                CreateUserLog((int)check.Id, 5, false, 3, "Wrong Code.");
                return Ok("Code is Invalid");
            }
        }
        else
        {
            CreateUserLog((int)check.Id, 5, false, 5, "Spam");
            return Ok("you Must Try 10 min later.");
        }

    }
    [HttpPut]
    [Authorize]
    public IActionResult UpdateUser([FromQuery] DtoUpdateUser Data)
    {
        Users check = db.Users_tbl.FirstOrDefault(x=>x.Username== User.FindFirstValue("username"));

        check.Addres = Data.Addres;
        check.FirstName = Data.FirstName;
        check.LastName = Data.LastName;
        check.Phone = Data.Phone;
        check.Profile = Uploadimage.Upload(Data.Profile);
        db.Users_tbl.Update(check);
        db.SaveChanges();
        return Ok("Done !");
    }

    private string CreateToken(string Username)
    {
        SymmetricSecurityKey secretKey = new SymmetricSecurityKey(Encoding.Default.GetBytes("SymmetricSecurityKey secretKey Encoding.Default.GetBytes"));
        SigningCredentials Credentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);

        Claim[] claims = new Claim[]{
            new Claim("username",Username)
        };

        var token = new JwtSecurityToken(
            issuer: "Issuer",
            audience: "Audience",
            claims: claims,
            expires: DateTime.Now.AddMinutes(30),
            signingCredentials: Credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);

    }
    private string SmsCode(string Code, string Phone)
    {
        // real sms
        // KavenegarApi SmsApi = new KavenegarApi(db.smsTokens.Find(1).Token);
        // SmsApi.VerifyLookup(Phone, Code, "demo");
        // return "Sms Sended";

        // price less
        return $"{Code} Sent to {Phone} .";
    }

    private void CreateUserLog(int UserId, int LogAction, bool isSucces, int Priority)
    {
        db.userLogs_tbl.Add(new UserLog
        {
            UserId = UserId,
            LogAction = LogAction,
            isSucces = isSucces,
            Priority = Priority,
            CreateDateTime = DateTime.Now
        });
        db.SaveChanges();
    }
    private void CreateUserLog(int UserId, int LogAction, bool isSucces, int Priority, string Description)
    {
        db.userLogs_tbl.Add(new UserLog
        {
            UserId = UserId,
            LogAction = LogAction,
            isSucces = isSucces,
            Priority = Priority,
            CreateDateTime = DateTime.Now,
            Description = Description
        });
        db.SaveChanges();
    }
}
