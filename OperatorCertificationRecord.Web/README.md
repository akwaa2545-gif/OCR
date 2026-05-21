# Operator's Tranining Database - Web Application

This is the modern web version of Operator's Training Database, converted from a Windows Forms desktop application to an ASP.NET Core Razor Pages web application.

## Features

- **Employee Management**: Add, update, and manage operator employee records
- **Training Records**: Track operator training and certifications
- **Skills Management**: Update and maintain operator skill qualifications
- **Disqualification Management**: Track operator disqualifications
- **Data Export**: Download various reports and data exports
- **Secure Login**: Employee authentication and session management
- **Photo Upload**: Support for employee photo uploads

## Technology Stack

- **Framework**: ASP.NET Core 8.0
- **UI**: Bootstrap 5.3, HTML5/CSS3, JavaScript
- **Database**: SQL Server (OperatorCertificationRecordDB)
- **Authentication**: Session-based with employee credentials

## Project Structure

```
OperatorCertificationRecord.Web/
├── Pages/                   # Razor Pages
│   ├── Login.cshtml        # Login page
│   ├── Dashboard.cshtml    # Main dashboard
│   ├── AddUser.cshtml      # Add employee page
│   ├── UpdateUser.cshtml   # Update employee page
│   ├── Shared/
│   │   └── _Layout.cshtml  # Main layout template
│   └── ...other pages
├── Services/               # Business logic and data access
│   └── DataServices.cs     # Database service classes
├── Models/                 # Data models
│   └── Employee.cs         # Employee model classes
├── wwwroot/               # Static files (CSS, JS, images)
├── appsettings.json       # Configuration settings
└── Program.cs             # Application startup

```

## Getting Started

### Prerequisites
- .NET 8.0 SDK or later
- SQL Server (with OperatorCertificationRecordDB database)
- Visual Studio 2022 or Visual Studio Code

### Configuration

1. Update the connection string in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;User Id=YOUR_USER;Password=YOUR_PASSWORD;Database=OperatorCertificationRecordDB;"
  }
}
```

2. Update the photo path if needed:
```json
{
  "PhotoPath": "\\\\YOUR_SERVER\\PhotoEmp$\\"
}
```

### Running the Application

1. **Using Visual Studio**:
   - Open the solution file
   - Set `OperatorCertificationRecord.Web` as the startup project
   - Press F5 to run

2. **Using .NET CLI**:
```bash
cd OperatorCertificationRecord.Web
dotnet restore
dotnet run
```

3. **Using Visual Studio Code**:
```bash
cd OperatorCertificationRecord.Web
dotnet run
```

The application will be available at `http://localhost:5000`

## Default Credentials

Use your employee ID and password to login. The default password for new employees is their employee ID.

## Features Implemented

### Fully Implemented
- ✅ Login and Authentication
- ✅ Dashboard
- ✅ Add Employee with photo upload
- ✅ Session management
- ✅ Responsive design

### Partially Implemented
- ⏳ Update Employee
- ⏳ Operator Training
- ⏳ Update Skills
- ⏳ Disqualification Management
- ⏳ Data Downloads

## Migration Notes

This application was converted from a Windows Forms application (`OperatorTrainingRecord.exe`) to a modern web application. Key changes:

1. **UI Migration**: All Windows Forms replaced with Razor Pages and Bootstrap
2. **Data Access**: Direct SqlDataAdapter calls replaced with async service classes
3. **Configuration**: app.config replaced with appsettings.json
4. **Photo Storage**: Network path storage maintained, but can be extended to database blob storage
5. **Security**: Session-based authentication; can be extended with ASP.NET Identity

## Future Enhancements

- Implement ASP.NET Identity for more robust security
- Add role-based access control (RBAC)
- Implement audit logging
- Create API endpoints for mobile app integration
- Add real-time notifications
- Migrate to Entity Framework Core ORM
- Add unit tests and integration tests
- Implement soft deletes for data protection
- Add advanced filtering and search capabilities
- Create admin panel for system configuration

## Database Schema

The application uses the following main tables:
- `tblEmployee` - Employee records
- `tblDepartment` - Department information
- `tblSection` - Section information
- `tblWorkshop` - Workshop information

Ensure your database has these tables with the appropriate schema before running the application.

## Troubleshooting

### Connection Issues
- Verify SQL Server is running and accessible
- Check connection string in appsettings.json
- Verify database exists and has proper schema

### Photo Upload Issues
- Check that the uploads folder exists or is created automatically
- Verify file permissions for the upload directory
- Check that the file size is within limits

## Support

For issues or questions, please refer to the original Windows Forms application documentation or contact the development team.

## License

This project maintains the same license as the original Windows Forms application.
