﻿using System;
using System.Collections.Generic;
using System.Text;

namespace StackExchange.Redis
{
    internal static class ExceptionFactory
    {
        private const string
            DataCommandKey = "redis-command",
            DataSentStatusKey = "request-sent-status",
            DataServerKey = "redis-server";

        internal static Exception AdminModeNotEnabled(bool includeDetail, RedisCommand command, Message message, ServerEndPoint server)
        {
            string s = GetLabel(includeDetail, command, message);
            var ex = new RedisCommandException("This operation is not available unless admin mode is enabled: " + s);
            if (includeDetail) AddDetail(ex, message, server, s);
            return ex;
        }

        internal static Exception CommandDisabled(RedisCommand command) => CommandDisabled(command.ToString());

        internal static Exception CommandDisabled(string command)
            => new RedisCommandException("This operation has been disabled in the command-map and cannot be used: " + command);

        internal static Exception TooManyArgs(string command, int argCount)
            => new RedisCommandException($"This operation would involve too many arguments ({argCount + 1} vs the redis limit of {PhysicalConnection.REDIS_MAX_ARGS}): {command}");

        internal static Exception ConnectionFailure(bool includeDetail, ConnectionFailureType failureType, string message, ServerEndPoint server)
        {
            var ex = new RedisConnectionException(failureType, message);
            if (includeDetail) AddDetail(ex, null, server, null);
            return ex;
        }

        internal static Exception DatabaseNotRequired(bool includeDetail, RedisCommand command)
        {
            string s = command.ToString();
            var ex = new RedisCommandException("A target database is not required for " + s);
            if (includeDetail) AddDetail(ex, null, null, s);
            return ex;
        }

        internal static Exception DatabaseOutfRange(bool includeDetail, int targetDatabase, Message message, ServerEndPoint server)
        {
            var ex = new RedisCommandException("The database does not exist on the server: " + targetDatabase);
            if (includeDetail) AddDetail(ex, message, server, null);
            return ex;
        }

        internal static Exception DatabaseRequired(bool includeDetail, RedisCommand command)
        {
            string s = command.ToString();
            var ex = new RedisCommandException("A target database is required for " + s);
            if (includeDetail) AddDetail(ex, null, null, s);
            return ex;
        }

        internal static Exception MasterOnly(bool includeDetail, RedisCommand command, Message message, ServerEndPoint server)
        {
            string s = GetLabel(includeDetail, command, message);
            var ex = new RedisCommandException("Command cannot be issued to a slave: " + s);
            if (includeDetail) AddDetail(ex, message, server, s);
            return ex;
        }

        internal static Exception MultiSlot(bool includeDetail, Message message)
        {
            var ex = new RedisCommandException("Multi-key operations must involve a single slot; keys can use 'hash tags' to help this, i.e. '{/users/12345}/account' and '{/users/12345}/contacts' will always be in the same slot");
            if (includeDetail) AddDetail(ex, message, null, null);
            return ex;
        }

        internal static string GetInnerMostExceptionMessage(Exception e)
        {
            if (e == null)
            {
                return "";
            }
            else
            {
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                }
                return e.Message;
            }
        }

        internal static Exception NoConnectionAvailable(bool includeDetail, bool includePerformanceCounters, RedisCommand command, Message message, ServerEndPoint server, ReadOnlySpan<ServerEndPoint> serverSnapshot)
        {
            string commandLabel = GetLabel(includeDetail, command, message);

            if (server != null)
            {
                //if we already have the serverEndpoint for connection failure use that
                //otherwise it would output state of all the endpoints
                serverSnapshot = new ServerEndPoint[] { server };
            }

            var innerException = PopulateInnerExceptions(serverSnapshot);

            StringBuilder exceptionmessage = new StringBuilder("No connection is available to service this operation: ").Append(commandLabel);
            string innermostExceptionstring = GetInnerMostExceptionMessage(innerException);
            if (!string.IsNullOrEmpty(innermostExceptionstring))
            {
                exceptionmessage.Append("; ").Append(innermostExceptionstring);
            }

            if (includeDetail)
            {
                exceptionmessage.Append("; ").Append(ConnectionMultiplexer.GetThreadPoolAndCPUSummary(includePerformanceCounters));
            }

            var ex = new RedisConnectionException(ConnectionFailureType.UnableToResolvePhysicalConnection, exceptionmessage.ToString(), innerException, message?.Status ?? CommandStatus.Unknown);

            if (includeDetail)
            {
                AddDetail(ex, message, server, commandLabel);
            }
            return ex;
        }

        internal static Exception PopulateInnerExceptions(ReadOnlySpan<ServerEndPoint> serverSnapshot)
        {
            var innerExceptions = new List<Exception>();
            if (serverSnapshot != null)
            {
                if (serverSnapshot.Length > 0 && serverSnapshot[0].Multiplexer.LastException != null)
                {
                    innerExceptions.Add(serverSnapshot[0].Multiplexer.LastException);
                }

                for (int i = 0; i < serverSnapshot.Length; i++)
                {
                    if (serverSnapshot[i].LastException != null)
                    {
                        var lastException = serverSnapshot[i].LastException;
                        innerExceptions.Add(lastException);
                    }
                }
            }
            if (innerExceptions.Count == 1)
            {
                return innerExceptions[0];
            }
            else if(innerExceptions.Count > 1)
            {
                return new AggregateException(innerExceptions);
            }
            return null;
        }

        internal static Exception NotSupported(bool includeDetail, RedisCommand command)
        {
            string s = GetLabel(includeDetail, command, null);
            var ex = new RedisCommandException("Command is not available on your server: " + s);
            if (includeDetail) AddDetail(ex, null, null, s);
            return ex;
        }

        internal static Exception NoCursor(RedisCommand command)
        {
            string s = GetLabel(false, command, null);
            return new RedisCommandException("Command cannot be used with a cursor: " + s);
        }

        internal static Exception Timeout(bool includeDetail, string errorMessage, Message message, ServerEndPoint server)
        {
            var ex = new RedisTimeoutException(errorMessage, message?.Status ?? CommandStatus.Unknown);
            if (includeDetail) AddDetail(ex, message, server, null);
            return ex;
        }

        private static void AddDetail(Exception exception, Message message, ServerEndPoint server, string label)
        {
            if (exception != null)
            {
                if (message != null)
                {
                    exception.Data.Add(DataCommandKey, message.CommandAndKey);
                    exception.Data.Add(DataSentStatusKey, message.Status);
                }
                else if (label != null)
                {
                    exception.Data.Add(DataCommandKey, label);
                }

                if (server != null) exception.Data.Add(DataServerKey, Format.ToString(server.EndPoint));
            }
        }

        private static string GetLabel(bool includeDetail, RedisCommand command, Message message)
        {
            return message == null ? command.ToString() : (includeDetail ? message.CommandAndKey : message.Command.ToString());
        }

        private static string GetLabel(bool includeDetail, string command, Message message)
        {
            return message == null ? command : (includeDetail ? message.CommandAndKey : message.Command.ToString());
        }
        internal static Exception UnableToConnect(ConnectionMultiplexer muxer, string failureMessage=null)
        {
            var sb = new StringBuilder("It was not possible to connect to the redis server(s).");
            if (muxer != null)
            {
                if (muxer.AuthSuspect) sb.Append(" There was an authentication failure; check that passwords (or client certificates) are configured correctly.");
                else if (!muxer.RawConfig.AbortOnConnectFail) sb.Append(" To create a disconnected multiplexer, disable AbortOnConnectFail.");
            }
            if (!string.IsNullOrWhiteSpace(failureMessage)) sb.Append(" ").Append(failureMessage.Trim());

            return new RedisConnectionException(ConnectionFailureType.UnableToConnect, sb.ToString());
        }

        internal static Exception BeganProfilingWithDuplicateContext(object forContext)
        {
            var exc = new InvalidOperationException("Attempted to begin profiling for the same context twice");
            exc.Data["forContext"] = forContext;
            return exc;
        }

        internal static Exception FinishedProfilingWithInvalidContext(object forContext)
        {
            var exc = new InvalidOperationException("Attempted to finish profiling for a context which is no longer valid, or was never begun");
            exc.Data["forContext"] = forContext;
            return exc;
        }
    }
}
