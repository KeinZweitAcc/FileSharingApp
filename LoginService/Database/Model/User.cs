using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;


namespace LoginService.Database.Model
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        public string Ip { get; set; } = null!;

        public int Port { get; set; } = 0;

        public string Username { get; set; } = null!;
    }
}
