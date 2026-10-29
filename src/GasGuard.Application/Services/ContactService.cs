using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed class ContactService(
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    IPhoneNumberNormalizer phoneNumbers,
    IClock clock,
    IUserContext userContext,
    ILogger<ContactService> logger) : IContactService
{
    public async Task<IReadOnlyList<ContactListItem>> GetContactsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var contacts = await unitOfWork.Contacts.ListAsync(includeInactive, cancellationToken);
        return contacts.Select(Map).ToArray();
    }

    public async Task<OperationResult> CreateAsync(ContactInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var validation = await ValidateAsync(unitOfWork, input, null, cancellationToken);
            if (validation.Errors.Count > 0)
            {
                return OperationResult.Fail("The contact was not created.", validation.Errors);
            }

            var now = clock.UtcNow;
            var contact = new Contact
            {
                Id = Guid.NewGuid(),
                FullName = input.FullName.Trim(),
                PhoneNumber = validation.PhoneNumber,
                Role = input.Role,
                IsActive = input.IsActive,
                NotifyOnCritical = input.NotifyOnCritical,
                NotifyOnWarning = input.NotifyOnWarning,
                NotifyOnCommunicationFailure = input.NotifyOnCommunicationFailure,
                Notes = CleanOptional(input.Notes),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            await unitOfWork.Contacts.AddAsync(contact, cancellationToken);
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "ContactCreated",
                "Contact",
                contact.Id.ToString(),
                AuditOutcome.Success,
                $"Contact {contact.FullName} created with role {contact.Role}. Critical notifications: {contact.NotifyOnCritical}."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok($"Contact {contact.FullName} was created.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create a contact.");
            return OperationResult.Fail("The contact could not be saved.");
        }
    }

    public async Task<OperationResult> UpdateAsync(
        Guid contactId,
        ContactInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var contact = await unitOfWork.Contacts.GetByIdAsync(contactId, cancellationToken);
            if (contact is null)
            {
                return OperationResult.Fail("The contact no longer exists.");
            }

            var validation = await ValidateAsync(unitOfWork, input, contactId, cancellationToken);
            if (validation.Errors.Count > 0)
            {
                return OperationResult.Fail("The contact was not updated.", validation.Errors);
            }

            contact.FullName = input.FullName.Trim();
            contact.PhoneNumber = validation.PhoneNumber;
            contact.Role = input.Role;
            contact.IsActive = input.IsActive;
            contact.NotifyOnCritical = input.NotifyOnCritical;
            contact.NotifyOnWarning = input.NotifyOnWarning;
            contact.NotifyOnCommunicationFailure = input.NotifyOnCommunicationFailure;
            contact.Notes = CleanOptional(input.Notes);
            var now = clock.UtcNow;
            contact.UpdatedAtUtc = now;
            unitOfWork.Contacts.Update(contact);
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "ContactUpdated",
                "Contact",
                contact.Id.ToString(),
                AuditOutcome.Success,
                $"Contact {contact.FullName} updated. Active: {contact.IsActive}."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok($"Contact {contact.FullName} was updated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update contact {ContactId}.", contactId);
            return OperationResult.Fail("The contact could not be updated.");
        }
    }

    public async Task<OperationResult> SetActiveAsync(
        Guid contactId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var contact = await unitOfWork.Contacts.GetByIdAsync(contactId, cancellationToken);
            if (contact is null)
            {
                return OperationResult.Fail("The contact no longer exists.");
            }

            if (contact.IsActive == active)
            {
                return OperationResult.Ok(active ? "The contact is already active." : "The contact is already inactive.");
            }

            var now = clock.UtcNow;
            contact.IsActive = active;
            contact.UpdatedAtUtc = now;
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                active ? "ContactActivated" : "ContactDeactivated",
                "Contact",
                contact.Id.ToString(),
                AuditOutcome.Success,
                $"Contact {contact.FullName} {(active ? "activated" : "deactivated")}."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok(active ? "Contact reactivated." : "Contact deactivated. The record was retained.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to change active state for contact {ContactId}.", contactId);
            return OperationResult.Fail("The contact state could not be changed.");
        }
    }

    private ContactListItem Map(Contact contact) => new(
        contact.Id,
        contact.FullName,
        contact.PhoneNumber,
        phoneNumbers.FormatForDisplay(contact.PhoneNumber),
        contact.Role,
        contact.IsActive,
        contact.NotifyOnCritical,
        contact.NotifyOnWarning,
        contact.NotifyOnCommunicationFailure,
        contact.Notes,
        contact.CreatedAtUtc,
        contact.UpdatedAtUtc);

    private async Task<(List<string> Errors, string PhoneNumber)> ValidateAsync(
        IGasGuardUnitOfWork unitOfWork,
        ContactInput input,
        Guid? excludingContactId,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var normalizedPhone = string.Empty;

        if (string.IsNullOrWhiteSpace(input.FullName))
        {
            errors.Add("A contact name is required.");
        }
        else if (input.FullName.Trim().Length > 120)
        {
            errors.Add("The contact name cannot exceed 120 characters.");
        }

        if (!phoneNumbers.TryNormalize(input.PhoneNumber, out normalizedPhone, out var phoneError))
        {
            errors.Add($"Phone number: {phoneError}");
        }
        else if (await unitOfWork.Contacts.PhoneExistsAsync(normalizedPhone, excludingContactId, cancellationToken))
        {
            errors.Add($"{phoneNumbers.FormatForDisplay(normalizedPhone)} is already assigned to another contact.");
        }

        if (!Enum.IsDefined(input.Role))
        {
            errors.Add("Select a valid contact role.");
        }

        if (input.Notes?.Length > 1000)
        {
            errors.Add("Contact notes cannot exceed 1000 characters.");
        }

        return (errors, normalizedPhone);
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
