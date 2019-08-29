import json
import urllib.parse
import boto3
import os
import sys
import ftplib
import io
from base64 import b64decode

ENCRYPTED = os.environ['password']
# Decrypt code should run once and variables stored outside of the function
# handler so that these are decrypted once per container
password = boto3.client('kms').decrypt(CiphertextBlob=b64decode(ENCRYPTED))['Plaintext'].decode('utf-8')

s3 = boto3.client('s3')
ip = os.environ['ip']
username = os.environ['user']
remote_directory = os.environ['remoteDirectory']

# https://pythonvibes.wordpress.com/2016/12/09/ftp-and-sftp-through-lambda/
# https://github.com/Vibish/FTP_SFTP_LAMBDA/blob/master/FTPThroughLambda.py
def FTPTransfer1(sourcekey, sourcebucket):
    #If we don't change the current working directory to /tmp/, while doing the FTP, LAMBDA tries to create the file
    #in the /tmp folder in the host as well - if we give download_path as the filename in storlines function.
    os.chdir("/tmp/")
    ftp = ftplib.FTP(ip)
    ftp.login(username, password)
    print(ftp.getwelcome())
    
    #Based on the Host FTP server, change the following statement to True or False        
    ftp.set_pasv(True)
    print('FTP set to Passive Mode')
    
    #Change the working directory in the host
    ftp.cwd(remote_directory)
    print('Working directory in host changed to {}'.format(remote_directory))

    #Download the file to /tmp/ folder        
    download_path = '/tmp/'+sourcekey
    s3.download_file(sourcebucket, sourcekey, download_path)
    print('File downloaded to local tmp directory')
       
    #Initiate File transfer     
    file = open(sourcekey,"rb")
    ftp.storbinary('STOR ' + sourcekey, file)
    print('File transmitted!!!')
    

# https://stackoverflow.com/questions/54290649/how-to-download-s3-file-in-serverless-lambda-python
def FTPTransfer2(sourcekey, sourcebucket):
    ftp = ftplib.FTP(ip)
    ftp.login(username, password)
    print(ftp.getwelcome())
    
    #Based on the Host FTP server, change the following statement to True or False        
    ftp.set_pasv(True)
    print('FTP set to Passive Mode')
    
    #Change the working directory in the host
    ftp.cwd(remote_directory)
    print('Working directory in host changed to {}'.format(remote_directory))
        
    #Download the file to memory stream
    data_stream = io.BytesIO()
    s3.download_fileobj(sourcebucket, sourcekey, data_stream)
    data_stream.seek(0)
    print('File downloaded to memory stream')
        
    #Initiate File transfer     
    ftp.storbinary("STOR " + sourcekey, data_stream)
    print('File transmitted!!!')
    
def lambda_handler(event, context):
    for record in event['Records']:
        sourcebucket = record['s3']['bucket']['name']
        print("BUCKET: " + sourcebucket)
        sourcekey = record['s3']['object']['key'] 
        print("FILE: " + sourcekey)
                                
        #FTP the file to the FTP host
        FTPTransfer2(sourcekey, sourcebucket)
        print('Transfered file {} from Source bucket {}'.format(sourcekey,sourcebucket))
        
        return 
    
