# ASP.NET Core Web Application File Reference

## Complete Project Structure

```
OperatorCertificationRecord/
│
├── OperatorCertificationRecord/                    [ORIGINAL: Windows Forms App]
│   ├── bin/
│   ├── obj/
│   ├── Properties/
│   ├── Resources/
│   ├── Service References/
│   ├── app.config
│   ├── ClassDiagram1.cd
│   ├── frmAddUser.cs (Designer.cs, .resx)
│   ├── frmDisqualification.cs (Designer.cs, .resx)
│   ├── frmDownLoadDisqualification.cs (Designer.cs, .resx)
│   ├── frmDownloadExpiryDate.cs (Designer.cs, .resx)
│   ├── frmDownloadResign.cs (Designer.cs, .resx)
│   ├── frmDownloadSection.cs (Designer.cs, .resx)
│   ├── frmOperatorTraining.cs (Designer.cs, .resx)
│   ├── frmUpdateSkill.cs (Designer.cs, .resx)
│   ├── frmUpdateUser.cs (Designer.cs, .resx)
│   ├── frmWelcome.cs (Designer.cs, .resx)
│   ├── Login.cs (Designer.cs, .resx)
│   ├── Number.cs
│   ├── OperatorCertificationRecord.csproj
│   ├── OperatorCertificationRecord.csproj.user
│   ├── Program.cs
│   └── Logo-OTR.ico
│
├── OperatorCertificationRecord.Web/                [NEW: ASP.NET Core Web App]
│   │
│   ├── Pages/                                      [Razor Pages (Views + Code-Behind)]
│   │   ├── Login.cshtml                           ← Employee login page
│   │   ├── Login.cshtml.cs                        ← Login logic (authentication)
│   │   │
│   │   ├── Dashboard.cshtml                       ← Main dashboard/menu
│   │   ├── Dashboard.cshtml.cs                    ← Dashboard logic
│   │   ├── ViewUser.cshtml                        ← Employee detail / skill history (Current, Disqualified, Obsoleted + Timeline)
│   │   ├── ViewUser.cshtml.cs                     ← ViewUser logic (timeline aggregation, PDF preview/download handler)
│   │   │
│   │   ├── AddUser.cshtml                         ← Add employee form (COMPLETE)
│   │   ├── AddUser.cshtml.cs                      ← Add employee logic (COMPLETE)
│   │   ├── AddSkill.cshtml                        ← Add Skill form (client + server validation, candidate certificate upload)
│   │   ├── AddSkill.cshtml.cs                     ← AddSkill logic (score validation, file save)
│   │   │
│   │   ├── UpdateUser.cshtml                      ← Update employee form
│   │   ├── UpdateUser.cshtml.cs                   ← Update employee logic
│   │   │
│   │   ├── OperatorTraining.cshtml                ← Training records page
│   │   ├── OperatorTraining.cshtml.cs             ← Training logic
│   │   │
│   │   ├── UpdateSkill.cshtml                     ← Skill updates page
│   │   ├── UpdateSkill.cshtml.cs                  ← Skill logic
│   │   │
│   │   ├── Disqualification.cshtml                ← Disqualification page
│   │   ├── Disqualification.cshtml.cs             ← Disqualification logic
│   │   │
│   │   ├── DownloadExpiryDate.cshtml              ← Export expiry dates
│   │   ├── DownloadResign.cshtml                  ← Export resignations
│   │   ├── DownloadSection.cshtml                 ← Export sections
│   │   ├── DownloadDisqualification.cshtml        ← Export disqualifications
│   │   ├── DownloadPages.cshtml.cs                ← Code-behind for all downloads
│   │   │
│   │   ├── Logout.cshtml                          ← Logout action page
│   │   ├── Logout.cshtml.cs                       ← Session cleanup
│   │   │
│   │   ├── Shared/                                [Shared Components]
│   │   │   ├── _Layout.cshtml                    ← Master layout template
│   │   │   └── _Layout.cshtml.css                ← Layout styles
│   │   │
│   │   ├── _ViewImports.cshtml                   ← Global page imports
│   │   ├── _ViewStart.cshtml                     ← Page initialization
│   │   │
│   │
│   ├── Controllers/                                [API Controllers]
│   │   └── DownloadController.cs                   ← API endpoints for downloads/exports (/api/download/*)
│   │
│   ├── Services/                                  [Business Logic & Data Access]
│   │   └── DataServices.cs                       ← Database operations (updated: retrieves certificate `Download` path for skills)
│   │       ├── EmployeeService                   ← Get/Add/Update employees (used by Dashboard/ViewUser)
│   │       ├── DepartmentService                 ← Department lookup
│   │       ├── SectionService                    ← Section lookup
│   │       └── WorkshopService                   ← Workshop lookup
│   │
│   ├── Models/                                   [Data Models]
│   │   └── Employee.cs
│   │       ├── Employee class                    ← Employee entity
│   │       ├── Department class                  ← Department entity
│   │       ├── Section class                     ← Section entity
│   │       └── Workshop class                    ← Workshop entity
│   │   └── EmployeeSkillRecord.cs                ← Skill record models (added `DownloadPath` property for certificate files)
│   │
│   ├── wwwroot/                                  [Static Files - Served to Browser]
│   │   ├── css/                                  ← Stylesheets
│   │   │   ├── app.css                           ← Custom app styles (timeline, alternating row colors)
│   │   │   └── [Bootstrap included via CDN]
│   │   │
│   │   ├── js/                                   ← JavaScript files
│   │   │   ├── app.js                            ← Client-side validation, PDF preview handlers
│   │   │   └── [Bootstrap included via CDN]
│   │   │   └── [Bootstrap included via CDN]
│   │   │
│   │   ├── images/                               ← Image assets
│   │   │   └── [Add project logos/images here]
│   │   │
│   │   └── uploads/                              ← Employee photos (auto-created)
│   │       └── [Employee photos stored here]
│   │
│   ├── Properties/                               [Project Properties]
│   │   ├── launchSettings.json                  ← Debug & release profiles
│   │   └── AssemblyInfo.cs                      ← [Generated by dotnet]
│   │
│   ├── Program.cs                                ← Main entry point
│   │                                             (Configure services, middleware)
│   │
│   ├── appsettings.json                          ← Configuration (Production)
│   │   ├── ConnectionStrings                    ← Database connection
│   │   ├── Logging                              ← Log levels
│   │   ├── AllowedHosts                         ← CORS hosts
│   │   └── PhotoPath                            ← Network path for photos
│   │
│   ├── appsettings.Development.json              ← Configuration (Development)
│   │
│   ├── OperatorCertificationRecord.Web.csproj   ← Project file (.NET 8.0)
│   │   ├── TargetFramework: net8.0
│   │   └── PackageReferences: SqlClient
│   │
│   ├── README.md                                 ← Full documentation
│   │
│   └── [bin/]                                    ← Compiled binaries
│       └── [obj/]                                ← Build artifacts
│
├── OperatorCertificationRecord.sln               [UPDATED: Solution file]
│   ├── OperatorCertificationRecord project       ← Windows Forms (x86)
│   └── OperatorCertificationRecord.Web project   ← ASP.NET Core (Any CPU)
│
├── .gitignore                                    [Git configuration]
│
├── QUICKSTART.md                                 [Quick start guide]
│
├── MIGRATION_SUMMARY.md                          [Migration details]
│
└── README.md                                     [Legacy - see new README]
```

## Page Navigation Map

```
┌─────────────────┐
│  Login Page     │ ← Start here
│  /Login         │
└────────┬────────┘
         │ (Valid credentials)
         ↓
┌─────────────────────────────┐
│  Dashboard                  │
│  /Dashboard                 │
│ (Navigation Hub)            │
└──┬──────────────────────────┘
   │
   ├─→ Add Employee (/AddUser)           ✅ COMPLETE
   │   ├─ Employee ID
   │   ├─ Personal Info (ENG/THAI)
   │   ├─ Organization (Dept/Section)
   │   └─ Photo Upload
   │
   ├─→ Update Employee (/UpdateUser)     ⏳ Ready
   │   └─ Modify existing records
   │
   ├─→ Operator Training (/OperatorTraining)
   │   └─ Training records
   │
   ├─→ Update Skill (/UpdateSkill)
   │   └─ Skill management
   │
   ├─→ Disqualification (/Disqualification)
   │   └─ Disqualification tracking
   │
   ├─→ Download Expiry Date (/DownloadExpiryDate)
   │   └─ Export expiry data
   │
   ├─→ Download Resign (/DownloadResign)
   │   └─ Export resignation data
   │
   ├─→ Download Section (/DownloadSection)
   │   └─ Export section reports
   │
   ├─→ Download Disqualification (/DownloadDisqualification)
   │   └─ Export disqualification data
   │
   └─→ Logout (/Logout)
       └─ Clear session & return to Login
```

  ## Recent Feature Summary

  - Timeline Tab (ViewUser): combines Current, Disqualified, and Obsoleted skill records into a single chronological view. Includes a UI-kit style left/right timeline, central vertical line, date boxes, and color-coded status markers.
  - PDF Preview & Download: uploaded certificate PDFs attached to skill records can be previewed in a modal (iframe) and downloaded. Handled by `ViewUser.cshtml.cs` (`OnGetDownloadFileAsync`) and `EmployeeSkillRecord.DownloadPath`.
  - Export/Download API & pages: `DownloadController` exposes `/api/download/*` endpoints (expiry, resign, disqualified, obsoleted, skillmatrix, operator-noskill, section, skillinventory). Client pages use preview/count endpoints and support server-side streaming/downloads where appropriate.
  - Alternating row colors: Current Skills table uses zebra striping for readability (CSS in `app.css`).
  - AddSkill validation: client-side (`app.js`) and server-side checks ensure score fields are 0–100; user-friendly messages replace generic HTML5 messages.
  - Models & Data: `EmployeeSkillRecord` updated with `DownloadPath`; `DataServices.GetCurrentSkillRecordsAsync` retrieves the `Download` column.

## Database Schema Reference

```
SQL Server Database: OperatorCertificationRecordDB
Server: svr120a
Login: TETUSR / TETPWD

Tables Used:
├── tblEmployee
│   ├── EmpCode (PK)
│   ├── EmpPassword
│   ├── JoinDate
│   ├── JobGrade
│   ├── HEng, PersonFnameEng, PersonLnameEng
│   ├── HThai, PersonFnameThai, PersonLnameThai
│   ├── DeptID (FK)
│   ├── SectID (FK)
│   ├── WorkshopID (FK)
│   ├── Shift
│   └── Photo (Path/Blob)
│
├── tblDepartment
│   ├── DeptID (PK)
│   └── DeptName
│
├── tblSection
│   ├── SectID (PK)
│   ├── SectName
│   └── DeptID (FK)
│
└── tblWorkshop
    ├── WorkshopID (PK)
    └── WorkshopName
```

## Configuration Files Reference

### Program.cs - Application Startup
```csharp
- Adds Razor Pages
- Adds Session services
- Registers Database services (Dependency Injection)
- Configures middleware pipeline
- Maps Razor Pages routes
```

### appsettings.json - Configuration
```json
{
  "ConnectionStrings": {},  ← Database connection
  "Logging": {},            ← Log levels
  "AllowedHosts": "*",     ← CORS configuration
  "PhotoPath": ""          ← Network path for photos
}
```

### launchSettings.json - Debug Profiles
```json
- http: localhost:5000
- https: localhost:44300
- IIS Express profile
```

## Key Dependencies

### NuGet Packages
- `Microsoft.AspNetCore.App` - Main ASP.NET Core framework
- `System.Data.SqlClient` - SQL Server connectivity

### CDN Resources (from HTML)
- Bootstrap 5.3 (CSS & JavaScript)
- No other external dependencies

## Important Directories

| Directory | Purpose | Served to |
|-----------|---------|-----------|
| `Pages/` | Razor Pages | Browser |
| `Services/` | Business Logic | Internal |
| `Models/` | Data Classes | Internal |
| `wwwroot/` | Static Assets | Browser |
| `bin/` | Compiled Code | Server |
| `obj/` | Build Artifacts | Build System |

## Running the Application

```bash
# Development
dotnet run
→ http://localhost:5000

# Build Release
dotnet publish -c Release -o ./publish
→ Ready for deployment
```

## File Size Information

| File Type | Count | Purpose |
|-----------|-------|---------|
| Razor Pages | 10 | User interface |
| Code-behind | 10 | Business logic |
| Controllers | 1 | API endpoints (DownloadController) |
| Service Classes | 4 | Data access |
| Models | 4 | Data structures |
| Config Files | 4 | Application settings |
| Documentation | 3 | User guides |

---

**Total Lines of Code**: ~3,000+  
**Status**: Production Ready (Core Features)  
**Last Updated**: January 26, 2026
