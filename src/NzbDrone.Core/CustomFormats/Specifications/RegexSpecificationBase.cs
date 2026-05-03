using System;
using System.Text.RegularExpressions;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public class RegexSpecificationBaseValidator : AbstractValidator<RegexSpecificationBase>
    {
        public RegexSpecificationBaseValidator()
        {
            RuleFor(c => c.Value).NotEmpty().WithMessage("Regex Pattern must not be empty");
        }
    }

    public abstract class RegexSpecificationBase : CustomFormatSpecificationBase
    {
        private static readonly RegexSpecificationBaseValidator Validator = new RegexSpecificationBaseValidator();

        // WR-09: catastrophic-backtracking / ReDoS DoS surface mitigation. A user-provided
        // pattern like ^(a+)+$ against a long input can burn CPU indefinitely; without a
        // timeout, every release evaluation against that CF blocks the decision pipeline.
        // 250ms is generous for normal patterns and aborts catastrophic ones cleanly via
        // RegexMatchTimeoutException (caught in MatchString and treated as no-match).
        private static readonly TimeSpan _regexTimeout = TimeSpan.FromMilliseconds(250);

        protected Regex _regex;
        protected string _raw;

        [FieldDefinition(1, Label = "CustomFormatsSpecificationRegularExpression", HelpText = "CustomFormatsSpecificationRegularExpressionHelpText")]
        public string Value
        {
            get => _raw;
            set
            {
                _raw = value;

                if (value.IsNotNullOrWhiteSpace())
                {
                    _regex = new Regex(value, RegexOptions.Compiled | RegexOptions.IgnoreCase, _regexTimeout);
                }
            }
        }

        protected bool MatchString(string compared)
        {
            if (compared == null || _regex == null)
            {
                return false;
            }

            try
            {
                return _regex.IsMatch(compared);
            }
            catch (RegexMatchTimeoutException)
            {
                // WR-09: pattern hit the catastrophic-backtracking timeout. Treat as no-match
                // so a single sloppy CF cannot freeze the entire decision pipeline.
                return false;
            }
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
