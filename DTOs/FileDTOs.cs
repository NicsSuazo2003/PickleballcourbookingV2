using System.ComponentModel.DataAnnotations;

namespace PickleballBookingSystem.DTOs;

public record DeleteFileRequest(
    [Required, MaxLength(2000)] string Url
);