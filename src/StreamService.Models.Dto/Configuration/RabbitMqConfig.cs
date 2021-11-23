using LT.DigitalOffice.Kernel.Configurations;

namespace LT.DigitalOffice.StreamService.Models.Dto.Configuration
{
  public class RabbitMqConfig : BaseRabbitMqConfig
  {
    public string UpdateSmtpCredentialsEndpoint { get; set; }
  }
}
