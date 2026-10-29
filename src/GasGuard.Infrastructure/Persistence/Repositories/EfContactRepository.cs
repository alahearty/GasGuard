using GasGuard.Application.Abstractions;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Persistence.Repositories;

internal sealed class EfContactRepository(GasGuardDbContext context) : IContactRepository
{
    public Task<Contact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Contacts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Contact>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.Contacts.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.FullName)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> PhoneExistsAsync(string normalizedPhone, Guid? excludingContactId, CancellationToken cancellationToken = default) =>
        context.Contacts.AnyAsync(
            x => x.PhoneNumber == normalizedPhone &&
                 (!excludingContactId.HasValue || x.Id != excludingContactId.Value),
            cancellationToken);

    public async Task AddAsync(Contact contact, CancellationToken cancellationToken = default) =>
        await context.Contacts.AddAsync(contact, cancellationToken);

    public void Update(Contact contact)
    {
        if (context.Entry(contact).State == EntityState.Detached)
        {
            context.Contacts.Update(contact);
        }
    }
}
