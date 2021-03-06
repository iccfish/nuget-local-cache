﻿# Nuget本地加速缓存服务

在Visual Studio 2013、2015中，使用的是Nuget包管理器对第三方组件进行管理升级的。这个东东就类似于NodeJS中的npm。
但是很可惜的是官方的nuget服务器是国外的服务器，由于种种众所周知以及客观原因，nuget包管理器在国内使用向来很慢。

## 原理介绍&使用说明

请参见我的博客说明：[提供针对Nuget包管理器的缓存加速服务](http://blog.fishlee.net/2015/10/14/announcing_nuget_acceleration_service/)

## 项目说明

项目是一个简单的Web项目，使用了一个自定义的HttpHandler捕获所有请求，并将对应路径的文件下载并缓存。
对于索引信息和API信息文件（*.JSON），在缓存并返回前，需要替换相对应的的地址（比如把```api.nuget.org```替换为```nugetcache.nuget.org```），因此针对部分搜索请求很难提供加速效果。

同时，缓存时会保存时间戳以及相对应的数据，会按天做时间戳校验，这里会使用ETag等信息提交源服务器做校验，因此设计上可能比官方的更好（官方并没有提交这些校验，始终会要求服务器返回当前的所有信息）。
对于包文件（```*.nupkg```），不会做校验，因为Nuget官方地址不同版本号的包地址是不同的，并且Nuget目前并不允许修改或重新上传包，因此没有检测同一地址包更新的必要。

## 开发平台说明

项目使用VS2015开发，基于.NET 4.5平台。源码未针对低版本VS的编译做检测，如果发现语法或部分地方错误，除非必要，还请自行修正。

另外，由于运行方式的制约，不建议将此服务和其它站点混合使用。

最后，如果可以的话，建议直接使用我提供的服务即可，参见之前的博客说明。

## 更多计划

- [ ] 引入运行统计支持

## 更新说明

###2016-08-04

* 增加忽略路径功能，允许忽略符合要求的路径，以便于加入其它资源

###2016.04.16

* 针对比较大的nupkg包优化，当服务器缓存需要较长时间下载时，缓存会在下载的同时向客户端发送数据


### 2016.04.06

* 对nupkg包下载禁用内存缓冲模式，改为临时文件保存，降低内存使用
* 支持新nuget管理器中的GZIP，对于JSON信息文件数据传输量有极大改善
* 替换域名部分去除硬编码的域名
* [本次更新详细说明](http://blog.fishlee.net/2016/04/07/nuget-cache-service-updated-20160406/)