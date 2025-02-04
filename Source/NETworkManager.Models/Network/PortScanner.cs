﻿using NETworkManager.Models.Lookup;
using NETworkManager.Utilities;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NETworkManager.Models.Network;

public class PortScanner
{
    #region Variables
    private int _progressValue;

    public bool ResolveHostname = true;
    public int HostThreads = 5;
    public int PortThreads = 100;
    public bool ShowClosed = false;
    public int Timeout = 4000;
    #endregion

    #region Events
    public event EventHandler<PortScannedArgs> PortScanned;

    protected virtual void OnPortScanned(PortScannedArgs e)
    {
        PortScanned?.Invoke(this, e);
    }

    public event EventHandler ScanComplete;

    protected virtual void OnScanComplete()
    {
        ScanComplete?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<ProgressChangedArgs> ProgressChanged;

    public virtual void OnProgressChanged()
    {
        ProgressChanged?.Invoke(this, new ProgressChangedArgs(_progressValue));
    }

    public event EventHandler UserHasCanceled;

    protected virtual void OnUserHasCanceled()
    {
        UserHasCanceled?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    #region Methods
    public void ScanAsync(IPAddress[] ipAddresses, int[] ports, CancellationToken cancellationToken)
    {
        _progressValue = 0;

        // Modify the ThreadPool for better performance
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.SetMinThreads(workerThreads + HostThreads + PortThreads, completionPortThreads + HostThreads + PortThreads);

        Task.Run(() =>
        {
            try
            {
                var hostParallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = HostThreads
                };

                var portParallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = PortThreads / HostThreads
                };

                Parallel.ForEach(ipAddresses, hostParallelOptions, ipAddress =>
                {
                    // Resolve Hostname (PTR)
                    var hostname = string.Empty;

                    if (ResolveHostname)
                    {
                        // Don't use await in Paralle.ForEach, this will break
                        var dnsResolverTask = DNSClient.GetInstance().ResolvePtrAsync(ipAddress);

                        // Wait for task inside a Parallel.Foreach
                        dnsResolverTask.Wait();

                        if (!dnsResolverTask.Result.HasError)
                            hostname = dnsResolverTask.Result.Value;
                    }

                    // Check each port
                    Parallel.ForEach(ports, portParallelOptions, port =>
                    {
                        // Test if port is open
                        using (var tcpClient = new TcpClient(ipAddress.AddressFamily))
                        {
                            PortState portState = PortState.None;
                            
                            try
                            {
                                var task = tcpClient.ConnectAsync(ipAddress, port);

                                if (task.Wait(Timeout))
                                    portState = tcpClient.Connected ? PortState.Open : PortState.Closed;
                                else
                                    portState = PortState.TimedOut;                                    
                            }
                            catch
                            {
                                portState = PortState.Closed;
                            }
                            finally
                            {
                                tcpClient?.Close();

                                if (ShowClosed || portState == PortState.Open)
                                    OnPortScanned(new PortScannedArgs(ipAddress, hostname, port, PortLookup.Lookup(port).FirstOrDefault(x => x.Protocol == PortLookup.Protocol.Tcp), portState));
                            }
                        }

                        IncreaseProgess();
                    });
                });
            }
            catch (OperationCanceledException) // If user has canceled
            {
                OnUserHasCanceled();
            }
            finally
            {
                // Reset the ThreadPool to defaul
                ThreadPool.GetMinThreads(out workerThreads, out completionPortThreads);
                ThreadPool.SetMinThreads(workerThreads - HostThreads + PortThreads, completionPortThreads - HostThreads + PortThreads);

                OnScanComplete();
            }
        }, cancellationToken);
    }

    private void IncreaseProgess()
    {
        // Increase the progress                        
        Interlocked.Increment(ref _progressValue);
        OnProgressChanged();
    }
    #endregion
}
