using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using APIHandler.Constants;
using System;

namespace APIHandler.Helpers;

public static class AwsClientFactory
{
    private static readonly AWSCredentials LocalCredentials = new BasicAWSCredentials("local", "local");

    public static AmazonDynamoDBClient CreateDynamoDb()
    {
        var config = new AmazonDynamoDBConfig();
        var endpoint = GetLocalEndpoint("LOCAL_DYNAMODB_ENDPOINT");
        if (endpoint is null)
            return new AmazonDynamoDBClient(ApplyRegion(config));

        return new AmazonDynamoDBClient(LocalCredentials, ApplyEndpoint(config, endpoint));
    }

    public static AmazonCognitoIdentityProviderClient CreateCognito()
    {
        var config = new AmazonCognitoIdentityProviderConfig();
        var endpoint = GetLocalEndpoint("LOCAL_AWS_ENDPOINT");
        if (endpoint is null)
            return new AmazonCognitoIdentityProviderClient(ApplyRegion(config));

        return new AmazonCognitoIdentityProviderClient(LocalCredentials, ApplyEndpoint(config, endpoint));
    }

    public static AmazonSQSClient CreateSqs()
    {
        var config = new AmazonSQSConfig();
        var endpoint = GetLocalEndpoint("LOCAL_AWS_ENDPOINT");
        if (endpoint is null)
            return new AmazonSQSClient(ApplyRegion(config));

        return new AmazonSQSClient(LocalCredentials, ApplyEndpoint(config, endpoint));
    }

    public static AmazonS3Client CreateS3()
    {
        var config = new AmazonS3Config();
        var endpoint = GetLocalEndpoint("LOCAL_AWS_ENDPOINT");
        if (endpoint is null)
            return new AmazonS3Client(ApplyRegion(config));

        config.ForcePathStyle = true;
        return new AmazonS3Client(LocalCredentials, ApplyEndpoint(config, endpoint));
    }

    private static string? GetLocalEndpoint(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
    }

    private static T ApplyRegion<T>(T config) where T : ClientConfig
    {
        config.RegionEndpoint = RegionEndpoint.GetBySystemName(ResourceNames.Region);
        return config;
    }

    private static T ApplyEndpoint<T>(T config, string endpoint) where T : ClientConfig
    {
        config.ServiceURL = endpoint;
        config.AuthenticationRegion = ResourceNames.Region;
        config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
        config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        return config;
    }
}
