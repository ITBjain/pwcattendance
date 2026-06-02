using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using PwcApi.Models;

public class ChildAttendance
{
    [Key]
    public int Id { get; set; }
    
    public int ChildEnrollmentId { get; set; }
    
    // The Foreign Key connecting to GroupVariation
    public int GroupVariationId { get; set; }
    
    public DateTime AttendanceDate { get; set; }
    public TimeSpan? CheckInTime { get; set; }
    public TimeSpan? CheckOutTime { get; set; }
    public bool IsPresent { get; set; }

    // 🔥 This is the reverse connection: This Attendance belongs to ONE Batch
    [ForeignKey("GroupVariationId")]
public GroupVariation? GroupVariation { get; set; }}