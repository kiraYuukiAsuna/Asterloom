using Asterloom.Modules.Mail.Model;
using Asterloom.Modules.Mail.Persistence;

namespace Asterloom.Modules.Infrastructure.Mail;

internal sealed class InMemoryMailStore : IMailStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, SmtpAccount> _accounts = [];
    private readonly Dictionary<Guid, MailDelivery> _deliveries = [];

    public Task<MailPage<SmtpAccount>> ListAccountsAsync(
        MailScope scope,
        MailPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var values = _accounts.Values
                .Where(item => item.Scope == scope)
                .Where(item => request.IncludeInactive || item.Status == MailAccountStatus.Active)
                .Where(item => request.Query.Length == 0
                    || item.Name.Contains(request.Query, StringComparison.OrdinalIgnoreCase)
                    || item.Host.Contains(request.Query, StringComparison.OrdinalIgnoreCase)
                    || item.FromAddress.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id)
                .Skip(request.Offset)
                .Take(request.PageSize + 1)
                .ToList();
            return Task.FromResult(Trim(values, request.PageSize));
        }
    }

    public Task<SmtpAccount?> GetAccountAsync(
        MailScope scope,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _accounts.TryGetValue(accountId, out var account) && account.Scope == scope
                    ? account
                    : null);
        }
    }

    public Task<bool> TryCreateAccountAsync(
        SmtpAccount account,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_accounts.ContainsKey(account.Id)
                || _accounts.Values.Any(item =>
                    item.Scope == account.Scope
                    && string.Equals(item.Name, account.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            _accounts.Add(account.Id, account);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateAccountAsync(
        SmtpAccount account,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_accounts.TryGetValue(account.Id, out var current)
                || current.Scope != account.Scope
                || current.Version != expectedVersion
                || _accounts.Values.Any(item =>
                    item.Id != account.Id
                    && item.Scope == account.Scope
                    && string.Equals(item.Name, account.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            _accounts[account.Id] = account;
            return Task.FromResult(true);
        }
    }

    public Task<MailPage<MailDelivery>> ListDeliveriesAsync(
        MailScope scope,
        MailDeliveryStatus? status,
        MailPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var values = _deliveries.Values
                .Where(item => item.Scope == scope && (status is null || item.Status == status))
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Skip(request.Offset)
                .Take(request.PageSize + 1)
                .ToList();
            return Task.FromResult(Trim(values, request.PageSize));
        }
    }

    public Task<MailDelivery?> GetDeliveryAsync(
        MailScope scope,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _deliveries.TryGetValue(deliveryId, out var delivery) && delivery.Scope == scope
                    ? delivery
                    : null);
        }
    }

    public Task<MailDelivery?> GetDeliveryByClientMessageIdAsync(
        MailScope scope,
        string clientMessageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_deliveries.Values.FirstOrDefault(item =>
                item.Scope == scope
                && string.Equals(
                    item.ClientMessageId,
                    clientMessageId,
                    StringComparison.Ordinal)));
        }
    }

    public Task<bool> TryCreateDeliveryAsync(
        MailDelivery delivery,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_deliveries.ContainsKey(delivery.Id)
                || _deliveries.Values.Any(item =>
                    item.Scope == delivery.Scope
                    && string.Equals(
                        item.ClientMessageId,
                        delivery.ClientMessageId,
                        StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            _deliveries.Add(delivery.Id, delivery);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryCompleteDeliveryAsync(
        MailDelivery delivery,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_deliveries.TryGetValue(delivery.Id, out var current)
                || current.Scope != delivery.Scope
                || current.Status != MailDeliveryStatus.Pending
                || delivery.Status == MailDeliveryStatus.Pending)
            {
                return Task.FromResult(false);
            }

            _deliveries[delivery.Id] = delivery;
            return Task.FromResult(true);
        }
    }

    private static MailPage<T> Trim<T>(List<T> items, int pageSize)
    {
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(items, hasMore);
    }
}
