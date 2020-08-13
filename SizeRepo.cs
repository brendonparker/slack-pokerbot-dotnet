using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace slack_pokerbot_dotnet
{
    public class SizeRepo
    {
        private readonly Dictionary<string, Dictionary<string, string>> _validSizes;

        private string IMAGE_LOCATION => Environment.GetEnvironmentVariable("IMAGE_LOCATION");

        public SizeRepo()
        {
            _validSizes = new Dictionary<string, Dictionary<string, string>>();
            _validSizes["f"] = new Dictionary<string, string>
                        {
                            { "0", $"{IMAGE_LOCATION}0.png" },
                            { "1", $"{IMAGE_LOCATION}1.png" },
                            { "2", $"{IMAGE_LOCATION}2.png" },
                            { "3", $"{IMAGE_LOCATION}3.png" },
                            { "5", $"{IMAGE_LOCATION}5.png" },
                            { "8", $"{IMAGE_LOCATION}8.png" },
                            { "13", $"{IMAGE_LOCATION}13.png" },
                            { "20", $"{IMAGE_LOCATION}20.png" },
                            { "40", $"{IMAGE_LOCATION}40.png" },
                            { "100", $"{IMAGE_LOCATION}100.png" },
                            { "?", $"{IMAGE_LOCATION}unsure.png" }
                        };

            _validSizes["s"] = new Dictionary<string, string>
                        {
                            { "1", $"{IMAGE_LOCATION}1.png" },
                            { "3", $"{IMAGE_LOCATION}3.png" },
                            { "5", $"{IMAGE_LOCATION}5.png" },
                            { "8", $"{IMAGE_LOCATION}8.png" },
                            { "?", $"{IMAGE_LOCATION}unsure.png" },
                        };

            _validSizes["t"] = new Dictionary<string, string>
                        {
                            { "s", $"{IMAGE_LOCATION}small.png" },
                            { "m", $"{IMAGE_LOCATION}medium.png" },
                            { "l", $"{IMAGE_LOCATION}large.png" },
                            { "xl", $"{IMAGE_LOCATION}extralarge.png" },
                            { "?" , $"{IMAGE_LOCATION}unsure.png" },
                        };

            _validSizes["m"] = new Dictionary<string, string>
                        {
                            { "1", $"{IMAGE_LOCATION}one.png" },
                            { "2", $"{IMAGE_LOCATION}two.png" },
                            { "3", $"{IMAGE_LOCATION}three.png" },
                            { "4", $"{IMAGE_LOCATION}four.png" },
                            { "5", $"{IMAGE_LOCATION}five.png" },
                            { "6", $"{IMAGE_LOCATION}six.png" },
                            { "7", $"{IMAGE_LOCATION}seven.png" },
                            { "8", $"{IMAGE_LOCATION}eight.png" },
                            { "2d", $"{IMAGE_LOCATION}twod.png" },
                            { "3d", $"{IMAGE_LOCATION}threed.png" },
                            { "4d", $"{IMAGE_LOCATION}fourd.png" },
                            { "5d", $"{IMAGE_LOCATION}fived.png" },
                            { "1.5w", $"{IMAGE_LOCATION}weekhalf.png" },
                            { "2w", $"{IMAGE_LOCATION}twow.png" },
                            { "?", $"{IMAGE_LOCATION}unsure.png" },
                        };
        }


        public Dictionary<string, string> GetSize(string size)
        {
            return _validSizes[size];
        }

        public bool IsValidSize(string size)
        {
            return _validSizes.ContainsKey(size);
        }

        public string ListOfValidSizes()
        {
            var keys = _validSizes.Keys;

            return string.Join(", ", keys.Take(keys.Count - 1)) + ", or " + keys.Last();
        }

        public string GetCompositeImage(string size)
        {
            switch (size)
            {
                case "f":
                    return $"{IMAGE_LOCATION}composite.png";
                case "s":
                    return $"{IMAGE_LOCATION}scomposite.png";
                case "t":
                    return $"{IMAGE_LOCATION}scomposite.png";
                case "m":
                    return $"{IMAGE_LOCATION}mcomposite.png";
            }
            return string.Empty;
        }
    }
}
