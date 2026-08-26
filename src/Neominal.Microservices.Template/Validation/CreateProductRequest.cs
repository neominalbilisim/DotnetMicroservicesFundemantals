using FluentValidation;

namespace Neominal.Microservices.Template.Validation;

/// <summary>
/// Senaryo: Cross-cutting concern olarak istek validasyonu (FluentValidation).
/// </summary>
public record CreateProductRequest(string Name, decimal Price);

public class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Ürün adı boş olamaz.")
            .MaximumLength(100).WithMessage("Ürün adı en fazla 100 karakter olabilir.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Fiyat sıfırdan büyük olmalıdır.");
    }
}
