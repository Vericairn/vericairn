# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

require 'json'
require 'net/http'
require 'psych'
require 'set'
require 'uri'

# Parse YAML's syntax tree, not lines: quoted keys, flow mappings, folded values and reusable jobs
# must receive the same check. Script bodies and comments are scalars, never workflow instructions.
module WorkflowActions
  PIN = /\A[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*@[0-9a-fA-F]{40}\z/
  OWNER = /\A(?:actions\/|github\/|NuGet\/login@)/
  # CI-08: each entry records its owner's and repository's GitHub ids beside the action, so a repository deleted and
  # re-created under the same name by another account, or a renamed or transferred one, no longer matches its entry.
  ENTRY = /\A(?<action>\S+)(?:\s+owner=(?<owner_id>[1-9][0-9]{0,18})\s+repository=(?<repository_id>[1-9][0-9]{0,18}))?\z/
  # A reusable workflow in this repository is checked like every other workflow here, so it needs no pin of its own.
  LOCAL_WORKFLOW = /\A\.\/\.github\/workflows\/([A-Za-z0-9_-]+\.ya?ml)\z/

  # Every entry of actions.allow with its line, owner, repository, commit and recorded ids; errors for malformed ones.
  def self.entries(list, errors)
    allowed = Set.new
    owners = {}
    repositories = {}
    entries = []
    File.readlines(list).each_with_index do |line, index|
      text = line.split('#', 2).first.to_s.strip
      next if text.empty?
      entry = ENTRY.match(text)
      action = entry ? entry[:action] : text.split(/\s+/).first
      if !PIN.match?(action) || !allowed.add?(action)
        errors << "actions.allow:#{index + 1}: invalid or duplicate full-SHA action: #{action}"
      elsif !OWNER.match?(action)
        errors << "actions.allow:#{index + 1}: action owner is not permitted: #{action}"
      elsif entry.nil? || entry[:owner_id].nil?
        errors << "actions.allow:#{index + 1}: record the owner and repository ids after the action as owner=<id> repository=<id>: #{action}"
      else
        owner, repository = action.split('@', 2).first.split('/', 3)
        name = "#{owner}/#{repository}".downcase
        if owners.fetch(owner.downcase, entry[:owner_id]) != entry[:owner_id] || repositories.fetch(name, entry[:repository_id]) != entry[:repository_id]
          errors << "actions.allow:#{index + 1}: another entry records different ids for #{owner}/#{repository}"
        end
        owners[owner.downcase] = entry[:owner_id]
        repositories[name] = entry[:repository_id]
        entries << { line: index + 1, action: action, owner: owner, repository: repository, commit: action.split('@', 2).last,
                     owner_id: Integer(entry[:owner_id], 10), repository_id: Integer(entry[:repository_id], 10) }
      end
    end
    [allowed, entries]
  end

  def self.check(directory)
    errors = []
    list = File.join(directory, 'actions.allow')
    return ['Missing actions.allow beside the workflows.'] unless File.file?(list)

    allowed, = entries(list, errors)
    files = Dir.glob(File.join(directory, '*.{yml,yaml}')).sort
    errors << 'No workflows found.' if files.empty?
    files.each do |path|
      begin
        visit(Psych.parse_stream(File.read(path)), File.basename(path), allowed, errors, directory)
      rescue Psych::Exception => e
        errors << "#{File.basename(path)}: invalid YAML: #{e.message}"
      end
    end
    errors
  end

  def self.visit(node, file, allowed, errors, directory)
    if node.is_a?(Psych::Nodes::Mapping)
      node.children.each_slice(2) do |key, value|
        next unless key.is_a?(Psych::Nodes::Scalar) && key.value == 'uses'
        action = value.is_a?(Psych::Nodes::Scalar) ? value.value : nil
        local = action && LOCAL_WORKFLOW.match(action)
        if local
          errors << "#{file}:#{key.start_line + 1}: no workflow #{local[1]} beside this one." unless File.file?(File.join(directory, local[1]))
        elsif action.nil? || !PIN.match?(action)
          errors << "#{file}:#{key.start_line + 1}: uses must name an action pinned to a full commit SHA."
        elsif !allowed.include?(action)
          errors << "#{file}:#{key.start_line + 1}: action is not in actions.allow: #{action}"
        end
      end
    end
    (node.children || []).each { |child| visit(child, file, allowed, errors, directory) }
  end

  # CI-08: each entry's recorded ids against the repository GitHub serves under its name, and its pinned commit in that
  # repository. fetch takes an API path and returns the parsed body, or nil when GitHub does not serve it as is.
  def self.check_identities(directory, fetch)
    errors = []
    list = File.join(directory, 'actions.allow')
    return ['Missing actions.allow beside the workflows.'] unless File.file?(list)

    _, found = entries(list, errors)
    return errors unless errors.empty?

    found.each do |entry|
      where = "actions.allow:#{entry[:line]}"
      repository = fetch.call("repos/#{entry[:owner]}/#{entry[:repository]}")
      if !repository.is_a?(Hash) || repository['full_name'].to_s.downcase != "#{entry[:owner]}/#{entry[:repository]}".downcase
        errors << "#{where}: GitHub serves no repository #{entry[:owner]}/#{entry[:repository]} under that name."
        next
      end
      if repository['id'] != entry[:repository_id] || repository.dig('owner', 'id') != entry[:owner_id]
        errors << "#{where}: #{entry[:owner]}/#{entry[:repository]} is repository #{repository['id']} of owner #{repository.dig('owner', 'id')}, not the recorded repository #{entry[:repository_id]} of owner #{entry[:owner_id]}."
        next
      end
      commit = fetch.call("repos/#{entry[:owner]}/#{entry[:repository]}/commits/#{entry[:commit]}")
      unless commit.is_a?(Hash) && commit['sha'].to_s.downcase == entry[:commit].downcase
        errors << "#{where}: #{entry[:owner]}/#{entry[:repository]} has no commit #{entry[:commit]}."
      end
    end
    errors
  end

  # GitHub's REST API with the workflow's token when one is given. A redirect, as for a renamed or transferred
  # repository, is not followed: the entry must name the repository as GitHub serves it.
  def self.github(path)
    uri = URI("https://api.github.com/#{path}")
    request = Net::HTTP::Get.new(uri)
    request['Accept'] = 'application/vnd.github+json'
    request['User-Agent'] = 'workflow-action-policy'
    token = ENV['GH_TOKEN'].to_s
    request['Authorization'] = "Bearer #{token}" unless token.empty?
    2.times do |attempt|
      response = Net::HTTP.start(uri.host, uri.port, use_ssl: true, open_timeout: 30, read_timeout: 30) { |http| http.request(request) }
      return JSON.parse(response.body) if response.code == '200'
      return nil if response.code.start_with?('3', '4') && response.code != '429'
      raise "GitHub answered #{response.code} for #{path}." if attempt == 1
      sleep 5
    end
  end
end

if $PROGRAM_NAME == __FILE__
  identities = !ARGV.delete('--identities').nil?
  directory = ARGV.fetch(0, File.expand_path('../workflows', __dir__))
  if identities
    errors = WorkflowActions.check_identities(directory, WorkflowActions.method(:github))
    abort errors.join("\n") unless errors.empty?
    puts "Every allowed action names the repository and owner GitHub serves under its recorded ids, and its pinned commit."
  else
    errors = WorkflowActions.check(directory)
    abort errors.join("\n") unless errors.empty?
    puts 'Every workflow action is allowed and pinned to its declared full commit SHA.'
  end
end
