using Microsoft.EntityFrameworkCore;

namespace EmergencyServices.Api.Models;

public class Location
{
    public string Street{get; set} = string.Empty;
    public string Address{get; set;} =string.Empty;
    public string PostalCode{get;set;} = string.empty;


    ///<summary>
    /// Calculates the distance in kilmoters to location
    /// </summary>
    
    public double DistanceKmTo(Location other)
    {
        throw new NotImplementedException();

    }
}