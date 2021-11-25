using LT.DigitalOffice.Kernel.BrokerSupport.Configurations;

namespace LT.DigitalOffice.StreamService.Models.Dto.Configuration
{
  public class RabbitMqConfig : BaseRabbitMqConfig
  {
    public string UpdateSmtpCredentialsEndpoint { get; set; }
  }
}
